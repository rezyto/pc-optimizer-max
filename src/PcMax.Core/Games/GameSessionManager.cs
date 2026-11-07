using PcMax.Core.Platform;
using PcMax.Core.Settings;
using PcMax.Core.Tweaks;

namespace PcMax.Core.Games;

/// <summary>
/// Game mode: when a game starts, apply its profile; when it stops, undo everything.
/// Also undoes leftovers at app start (e.g. after a crash or power cut) via <see cref="RecoverAsync"/>.
/// </summary>
public sealed class GameSessionManager
{
    private readonly TweakEngine _engine;
    private readonly IProcessBooster _booster;
    private readonly Func<AppSettings> _settings;
    private readonly object _gate = new();
    private Session? _current;

    private sealed record Session(GameEvent Game, string? PreviousPriority, IReadOnlyList<int> SuspendedPids);

    public GameSessionManager(TweakEngine engine, IProcessBooster booster, Func<AppSettings> settings)
    {
        _engine = engine;
        _booster = booster;
        _settings = settings;
    }

    public event EventHandler<string>? StatusChanged;
    public GameEvent? CurrentGame { get { lock (_gate) return _current?.Game; } }

    /// <summary>Called by the game watcher. Only one game session at a time.</summary>
    public async Task OnGameStartedAsync(GameEvent game, CancellationToken ct = default)
    {
        var s = _settings();
        if (!s.AutoGameMode) return;
        var profile = FindProfile(s, game.ExecutableName);
        if (profile is { Enabled: false }) return;
        lock (_gate)
        {
            if (_current is not null) return;
            _current = new Session(game, null, Array.Empty<int>());
        }
        profile ??= new GameProfile { ExecutableName = game.ExecutableName, DisplayName = game.DisplayName ?? game.ExecutableName };

        foreach (var id in profile.TweakIds)
        {
            if (_engine.Find(id) is null) continue;
            await _engine.ApplyAsync(id, ct).ConfigureAwait(false);
        }

        string? prev = null;
        if (profile.HighPriority)
        {
            try { prev = _booster.BoostGame(game.ProcessId); } catch { /* process may have exited */ }
        }

        IReadOnlyList<int> suspended = Array.Empty<int>();
        if (profile.SuspendBackgroundApps && s.BackgroundAppsToSuspend.Count > 0)
        {
            try { suspended = _booster.SuspendBackground(s.BackgroundAppsToSuspend); } catch { }
        }

        lock (_gate) _current = new Session(game, prev, suspended);
        StatusChanged?.Invoke(this, $"Modo jogo ativo: {profile.DisplayName}");
    }

    public async Task OnGameStoppedAsync(GameEvent game, CancellationToken ct = default)
    {
        Session? session;
        lock (_gate)
        {
            if (_current is null || _current.Game.ProcessId != game.ProcessId) return;
            session = _current;
            _current = null;
        }
        await EndSessionAsync(session, ct).ConfigureAwait(false);
        StatusChanged?.Invoke(this, "Modo jogo terminado: tudo reposto.");
    }

    /// <summary>Emergency stop (e.g. critical temperature): undo the game session right away.</summary>
    public async Task AbortAsync(CancellationToken ct = default)
    {
        Session? session;
        lock (_gate) { session = _current; _current = null; }
        if (session is not null) await EndSessionAsync(session, ct).ConfigureAwait(false);
        else await _engine.RevertGameSessionAsync(ct).ConfigureAwait(false);
    }

    /// <summary>At startup: undo game-session tweaks left over from a crash or power cut.</summary>
    public Task RecoverAsync(CancellationToken ct = default) => _engine.RevertGameSessionAsync(ct);

    private async Task EndSessionAsync(Session session, CancellationToken ct)
    {
        try { _booster.ResumeAll(session.SuspendedPids); } catch { }
        try { _booster.RestoreGame(session.Game.ProcessId, session.PreviousPriority); } catch { }
        await _engine.RevertGameSessionAsync(ct).ConfigureAwait(false);
    }

    private static GameProfile? FindProfile(AppSettings s, string exe) =>
        s.Games.FirstOrDefault(g => string.Equals(g.ExecutableName, exe, StringComparison.OrdinalIgnoreCase));
}
