using System.Diagnostics;
using System.Runtime.Versioning;
using PcMax.Core.Games;
using PcMax.Core.Settings;

namespace PcMax.Windows.Games;

/// <summary>
/// Polls the process list every 2 s and raises <see cref="GameStarted"/>/<see cref="GameStopped"/>
/// for executables in <see cref="KnownGames.All"/> or the user's game profiles.
/// Only process names and ids are read (cheap: one NtQuerySystemInformation snapshot).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ProcessGameWatcher : IGameWatcher
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    private readonly Func<AppSettings> _settings;
    private readonly object _gate = new();
    private readonly Dictionary<int, GameEvent> _running = new();
    private Timer? _timer;
    private int _polling; // 0/1 guard against overlapping ticks
    private bool _disposed;

    public ProcessGameWatcher(Func<AppSettings> settings) => _settings = settings;

    public event EventHandler<GameEvent>? GameStarted;
    public event EventHandler<GameEvent>? GameStopped;

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _timer is not null) return;
            _timer = new Timer(_ => Poll(), null, TimeSpan.Zero, Interval);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
            // Forget what we saw: a later Start() reports running games again.
            _running.Clear();
        }
    }

    private void Poll()
    {
        if (Interlocked.Exchange(ref _polling, 1) == 1) return;
        try
        {
            var names = GameNames();
            var current = new Dictionary<int, string>();
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    var exe = p.ProcessName + ".exe";
                    if (names.ContainsKey(exe)) current[p.Id] = exe;
                }
                catch { /* exited while enumerating */ }
                finally { p.Dispose(); }
            }

            var started = new List<GameEvent>();
            var stopped = new List<GameEvent>();
            lock (_gate)
            {
                if (_timer is null) return; // stopped meanwhile
                foreach (var (pid, ev) in _running.ToList())
                {
                    // Gone, or the pid now belongs to another executable.
                    if (!current.TryGetValue(pid, out var exe) || !exe.Equals(ev.ExecutableName, StringComparison.OrdinalIgnoreCase))
                    {
                        _running.Remove(pid);
                        stopped.Add(ev);
                    }
                }
                foreach (var (pid, exe) in current)
                {
                    if (_running.ContainsKey(pid)) continue;
                    var ev = new GameEvent(exe, pid, names[exe]);
                    _running[pid] = ev;
                    started.Add(ev);
                }
            }

            foreach (var ev in stopped) Raise(GameStopped, ev);
            foreach (var ev in started) Raise(GameStarted, ev);
        }
        catch (Exception ex)
        {
            Log.Warn("Detetor de jogos: falha na verificação de processos", ex);
        }
        finally
        {
            Volatile.Write(ref _polling, 0);
        }
    }

    private void Raise(EventHandler<GameEvent>? handler, GameEvent ev)
    {
        if (handler is null) return;
        foreach (var d in handler.GetInvocationList())
        {
            try { ((EventHandler<GameEvent>)d)(this, ev); }
            catch (Exception ex) { Log.Error($"Handler de jogo falhou ({ev.ExecutableName})", ex); }
        }
    }

    /// <summary>exe name (with .exe) -> display name. User profiles override the built-in list.</summary>
    private Dictionary<string, string?> GameNames()
    {
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (exe, name) in KnownGames.All) map[exe] = name;
        try
        {
            foreach (var g in _settings().Games ?? Array.Empty<GameProfile>())
            {
                var exe = g.ExecutableName?.Trim();
                if (string.IsNullOrEmpty(exe)) continue;
                exe = Path.GetFileName(exe);
                if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) exe += ".exe";
                map[exe] = string.IsNullOrWhiteSpace(g.DisplayName) ? map.GetValueOrDefault(exe) ?? exe : g.DisplayName;
            }
        }
        catch (Exception ex) { Log.Warn("Detetor de jogos: definições indisponíveis", ex); }
        return map;
    }

    public void Dispose()
    {
        lock (_gate) _disposed = true;
        Stop();
    }
}
