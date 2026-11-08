using PcMax.Core.Platform;
using PcMax.Core.Settings;
using PcMax.Core.Tweaks;

namespace PcMax.Core.Games;

/// <summary>
/// Game mode: when a game starts, apply its profile; when it stops, undo everything.
/// Also undoes leftovers at app start (e.g. after a crash or power cut) via <see cref="RecoverAsync"/>.
/// <para>
/// Start, stop and abort run one at a time (<see cref="_ops"/>): a stop that arrives while the start is
/// still applying waits for it and then undoes all of it, so nothing (suspended apps, power plan)
/// is left behind for a game that is already gone.
/// </para>
/// </summary>
public sealed class GameSessionManager
{
    private readonly TweakEngine _engine;
    private readonly IProcessBooster _booster;
    private readonly Func<AppSettings> _settings;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _ops = new(1, 1);
    private Session? _current;
    private GameEvent? _starting;

    private sealed record Session(GameEvent Game, string? PreviousPriority, IReadOnlyList<int> SuspendedPids);

    public GameSessionManager(TweakEngine engine, IProcessBooster booster, Func<AppSettings> settings)
    {
        _engine = engine;
        _booster = booster;
        _settings = settings;
    }

    public event EventHandler<string>? StatusChanged;

    /// <summary>The game whose session is active or being started.</summary>
    public GameEvent? CurrentGame { get { lock (_gate) return _current?.Game ?? _starting; } }

    /// <summary>Called by the game watcher. Only one game session at a time.</summary>
    public async Task OnGameStartedAsync(GameEvent game, CancellationToken ct = default)
    {
        var s = _settings();
        if (!s.AutoGameMode) return;
        var profile = FindProfile(s, game.ExecutableName);
        if (profile is { Enabled: false }) return;
        lock (_gate)
        {
            // Cheap early exit; re-checked under _ops below.
            if (_current is not null || _starting is not null) return;
            _starting = game;
        }

        string? status = null;
        try
        {
            await _ops.WaitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            lock (_gate) if (ReferenceEquals(_starting, game)) _starting = null;
            throw;
        }
        try
        {
            lock (_gate)
            {
                if (!ReferenceEquals(_starting, game)) return; // a stop/abort for this game came first
                if (_current is not null) { _starting = null; return; }
            }
            profile ??= new GameProfile { ExecutableName = game.ExecutableName, DisplayName = game.DisplayName ?? game.ExecutableName };

            string? prev = null;
            IReadOnlyList<int> suspended = Array.Empty<int>();
            try
            {
                foreach (var id in profile.TweakIds ?? Array.Empty<string>())
                {
                    if (!StillStarting(game)) break;
                    // Only session tweaks: anything else would never be undone when the game closes.
                    if (_engine.Find(id) is not { GameSessionOnly: true }) continue;
                    await _engine.ApplyAsync(id, CancellationToken.None).ConfigureAwait(false);
                }

                if (profile.HighPriority && StillStarting(game))
                {
                    try { prev = _booster.BoostGame(game.ProcessId); } catch { /* process may have exited */ }
                }

                if (profile.SuspendBackgroundApps && s.BackgroundAppsToSuspend.Count > 0 && StillStarting(game))
                {
                    try { suspended = _booster.SuspendBackground(s.BackgroundAppsToSuspend, game.ProcessId); } catch { }
                }
            }
            catch
            {
                lock (_gate) if (ReferenceEquals(_starting, game)) _starting = null;
                await EndSessionAsync(new Session(game, prev, suspended)).ConfigureAwait(false);
                throw;
            }

            var session = new Session(game, prev, suspended);
            bool keep;
            lock (_gate)
            {
                keep = ReferenceEquals(_starting, game) && SafeIsRunning(game);
                _starting = null;
                if (keep) _current = session;
            }
            if (!keep)
            {
                // The game closed (or the session was aborted) while we were applying: undo it all now.
                await EndSessionAsync(session).ConfigureAwait(false);
                return;
            }
            status = $"Modo jogo ativo: {profile.DisplayName}";
        }
        finally
        {
            _ops.Release();
        }
        StatusChanged?.Invoke(this, status);
    }

    public async Task OnGameStoppedAsync(GameEvent game, CancellationToken ct = default)
    {
        lock (_gate)
        {
            // Stop while the start is still applying: tell the start to undo itself when it finishes.
            if (_starting is { } st && st.ProcessId == game.ProcessId) { _starting = null; return; }
            if (_current is null || _current.Game.ProcessId != game.ProcessId) return;
        }
        await _ops.WaitAsync(ct).ConfigureAwait(false);
        Session? session;
        try
        {
            lock (_gate)
            {
                if (_current is null || _current.Game.ProcessId != game.ProcessId) return;
                session = _current;
                _current = null;
            }
            await EndSessionAsync(session).ConfigureAwait(false);
        }
        finally
        {
            _ops.Release();
        }
        StatusChanged?.Invoke(this, "Modo jogo terminado: tudo reposto.");
    }

    /// <summary>
    /// Emergency stop (critical temperature, exit, game mode switched off): undo the game session, or any
    /// game-session tweak still applied without a session. A start in progress undoes itself when it finishes.
    /// </summary>
    public async Task AbortAsync(CancellationToken ct = default)
    {
        lock (_gate) _starting = null;
        await _ops.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Session? session;
            lock (_gate) { session = _current; _current = null; }
            if (session is not null) await EndSessionAsync(session).ConfigureAwait(false);
            else await _engine.RevertGameSessionAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _ops.Release();
        }
    }

    /// <summary>At startup: undo game-session tweaks left over from a crash or power cut.</summary>
    public Task RecoverAsync(CancellationToken ct = default) => _engine.RevertGameSessionAsync(ct);

    private bool StillStarting(GameEvent game)
    {
        lock (_gate) return ReferenceEquals(_starting, game);
    }

    private bool SafeIsRunning(GameEvent game)
    {
        try { return _booster.IsRunning(game.ProcessId, game.ExecutableName); }
        catch { return true; }
    }

    /// <summary>Always runs to the end (never cancelled): a half-undone session is the worst outcome.</summary>
    private async Task EndSessionAsync(Session session)
    {
        try { _booster.ResumeAll(session.SuspendedPids); } catch { }
        try { _booster.RestoreGame(session.Game.ProcessId, session.PreviousPriority); } catch { }
        await _engine.RevertGameSessionAsync(CancellationToken.None).ConfigureAwait(false);
    }

    internal static GameProfile? FindProfile(AppSettings s, string exe)
    {
        var wanted = ExeKey(exe);
        return s.Games.FirstOrDefault(g => g is not null && string.Equals(ExeKey(g.ExecutableName), wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>"C:\Games\cs2.EXE", "cs2.exe" and "cs2" all compare as "cs2".</summary>
    private static string ExeKey(string? exe)
    {
        var n = (exe ?? "").Trim();
        var slash = n.LastIndexOfAny(new[] { '\\', '/' });
        if (slash >= 0) n = n[(slash + 1)..];
        if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = n[..^4];
        return n.Trim();
    }
}
