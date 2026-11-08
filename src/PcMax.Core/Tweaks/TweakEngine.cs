namespace PcMax.Core.Tweaks;

/// <summary>
/// Applies and reverts tweaks with a safety net:
/// - refuses tweaks above the allowed risk level,
/// - saves the snapshot to disk BEFORE reporting success, so a crash can still be undone,
/// - if applying fails, it tries to restore what was there,
/// - never applies the same tweak twice (the first snapshot is the true original).
/// </summary>
public sealed class TweakEngine
{
    private readonly ISnapshotStore _store;
    private readonly Dictionary<string, ITweak> _tweaks;
    private readonly List<TweakSnapshot> _active;
    private readonly List<HistoryEntry> _history = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    public TweakEngine(IEnumerable<ITweak> tweaks, ISnapshotStore store)
    {
        _store = store;
        _tweaks = new Dictionary<string, ITweak>(StringComparer.Ordinal);
        foreach (var t in tweaks)
        {
            if (!_tweaks.TryAdd(t.Id, t))
                throw new ArgumentException($"Duplicate tweak id '{t.Id}'.", nameof(tweaks));
        }
        _active = new List<TweakSnapshot>();
        IReadOnlyList<TweakSnapshot> loaded;
        try { loaded = store.LoadActive(); }
        catch { loaded = Array.Empty<TweakSnapshot>(); }
        foreach (var snap in loaded)
        {
            if (snap is null || string.IsNullOrEmpty(snap.TweakId) || snap.PreviousValues is null) { DiscardedSnapshots++; continue; }
            // Unknown ids are kept (so RevertCore can drop them with a history entry); known ones must validate.
            if (_tweaks.TryGetValue(snap.TweakId, out var t) && !SafeAccepts(t, snap)) { DiscardedSnapshots++; continue; }
            if (_active.Any(a => a.TweakId == snap.TweakId)) { DiscardedSnapshots++; continue; } // only the first is the true original
            _active.Add(snap);
        }
        if (DiscardedSnapshots > 0)
        {
            try { store.SaveActive(_active); } catch { /* best effort */ }
        }
    }

    /// <summary>Snapshots read from disk that were rejected as invalid or tampered (never reverted).</summary>
    public int DiscardedSnapshots { get; }

    private static bool SafeAccepts(ITweak t, TweakSnapshot s)
    {
        try { return t.AcceptsSnapshot(s); }
        catch { return false; }
    }

    public event EventHandler<HistoryEntry>? HistoryAdded;

    public IReadOnlyCollection<ITweak> Tweaks => _tweaks.Values;
    public IReadOnlyList<HistoryEntry> History => _history.ToList();
    public IReadOnlyList<TweakSnapshot> ActiveSnapshots => _active.ToList();
    public RiskLevel MaxAllowedRisk { get; set; } = RiskLevel.Safe;

    public bool IsActive(string tweakId) => _active.Any(s => s.TweakId == tweakId);
    public ITweak? Find(string id) => _tweaks.GetValueOrDefault(id);

    public async Task<HistoryEntry> ApplyAsync(string tweakId, CancellationToken ct = default)
    {
        var tweak = Find(tweakId) ?? throw new KeyNotFoundException($"Unknown tweak '{tweakId}'.");
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (tweak.Risk > MaxAllowedRisk)
                return Record(tweak, HistoryAction.SkippedRisk, $"Risco {tweak.Risk} acima do permitido ({MaxAllowedRisk}).");

            if (IsActive(tweakId))
                return Record(tweak, HistoryAction.Applied, "Já estava ativo.");

            bool supported;
            try { supported = await tweak.IsSupportedAsync(ct).ConfigureAwait(false); }
            catch (Exception ex) { return Record(tweak, HistoryAction.Failed, ex.Message); }
            if (!supported)
                return Record(tweak, HistoryAction.SkippedUnsupported, "Não suportado neste PC.");

            TweakSnapshot snapshot;
            try
            {
                snapshot = await tweak.ApplyAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return Record(tweak, HistoryAction.Failed, ex.Message);
            }

            if (snapshot.TweakId != tweak.Id)
                snapshot = snapshot with { TweakId = tweak.Id };

            _active.Add(snapshot);
            try
            {
                _store.SaveActive(_active);
            }
            catch (Exception ex)
            {
                // Cannot persist the undo information: undo now rather than leave an untracked change.
                _active.Remove(snapshot);
                try { await tweak.RevertAsync(snapshot, CancellationToken.None).ConfigureAwait(false); } catch { /* best effort */ }
                return Record(tweak, HistoryAction.Failed, "Não foi possível guardar o estado anterior: " + ex.Message);
            }
            return Record(tweak, HistoryAction.Applied);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<HistoryEntry> RevertAsync(string tweakId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await RevertCoreAsync(tweakId, ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Reverts every active tweak, newest first. Continues even if one fails.</summary>
    public async Task<IReadOnlyList<HistoryEntry>> RevertAllAsync(Func<TweakSnapshot, bool>? filter = null, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var results = new List<HistoryEntry>();
            foreach (var snap in _active.AsEnumerable().Reverse().ToList())
            {
                if (filter is not null && !filter(snap)) continue;
                results.Add(await RevertCoreAsync(snap.TweakId, ct).ConfigureAwait(false));
            }
            return results;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Reverts only the tweaks that are meant to last while a game runs.</summary>
    public Task<IReadOnlyList<HistoryEntry>> RevertGameSessionAsync(CancellationToken ct = default) =>
        RevertAllAsync(s => _tweaks.TryGetValue(s.TweakId, out var t) && t.GameSessionOnly, ct);

    private async Task<HistoryEntry> RevertCoreAsync(string tweakId, CancellationToken ct)
    {
        var snapshot = _active.LastOrDefault(s => s.TweakId == tweakId);
        var tweak = Find(tweakId);
        if (snapshot is null)
            return Record(tweakId, tweak?.Name ?? tweakId, HistoryAction.Reverted, "Não estava ativo.");
        if (tweak is null)
        {
            // Tweak removed from this version of the app: drop the orphan so it does not block anything.
            _active.Remove(snapshot);
            _store.SaveActive(_active);
            return Record(tweakId, tweakId, HistoryAction.Failed, "Ajuste desconhecido nesta versão; registo removido.");
        }

        try
        {
            await tweak.RevertAsync(snapshot, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Record(tweak, HistoryAction.Failed, "Falha ao reverter: " + ex.Message);
        }
        _active.Remove(snapshot);
        _store.SaveActive(_active);
        return Record(tweak, HistoryAction.Reverted);
    }

    private HistoryEntry Record(ITweak tweak, HistoryAction action, string? detail = null) =>
        Record(tweak.Id, tweak.Name, action, detail);

    private HistoryEntry Record(string id, string name, HistoryAction action, string? detail)
    {
        var entry = new HistoryEntry(DateTimeOffset.UtcNow, id, name, action, detail);
        _history.Add(entry);
        if (_history.Count > 1000) _history.RemoveAt(0);
        HistoryAdded?.Invoke(this, entry);
        return entry;
    }
}
