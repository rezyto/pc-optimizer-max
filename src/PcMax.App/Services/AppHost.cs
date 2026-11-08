using System.Collections.Concurrent;
using PcMax.Core.Alerts;
using PcMax.Core.Games;
using PcMax.Core.Platform;
using PcMax.Core.Sensors;
using PcMax.Core.Settings;
using PcMax.Core.Tweaks;

namespace PcMax.App.Services;

/// <summary>
/// Composition root and background runtime: sensors every second, alerts, game watcher → game mode,
/// settings side effects (autostart, risk level, thresholds). UI-facing events are raised on the UI thread.
/// </summary>
public sealed class AppHost : IDisposable
{
    private readonly ConcurrentDictionary<int, GameEvent> _running = new();
    private readonly Queue<(DateTimeOffset At, double? Cpu, double? Gpu)> _recentTemps = new();
    private readonly object _tempsGate = new();
    private Timer? _sensorTimer;
    private int _polling;
    private bool _disposed;

    public AppHost(AppPaths paths, SettingsService settings, PlatformServices platform, INotifier notifier,
        IUiDispatcher ui, ILoadGenerator load, ISnapshotStore snapshots)
    {
        Paths = paths;
        Settings = settings;
        Platform = platform;
        Notifier = notifier;
        Ui = ui;

        Engine = new TweakEngine(platform.Tweaks, snapshots) { MaxAllowedRisk = settings.Current.MaxRisk };
        Games = new GameSessionManager(Engine, platform.Booster, () => Settings.Current);
        Alerts = new AlertEngine(settings.Current.Temperatures);
        Health = new HealthService(platform.Hardware, RecentMaxTemps);
        Guard = new ChangeGuard(platform.RestorePoints, settings);
        Optimizer = new OptimizerService(Health, Engine, Guard);
        Network = new NetworkTester(platform.Network, load);
        Benchmark = new BenchmarkService(platform.FrameCapture, Engine, Guard, pid => Games.CurrentGame?.ProcessId == pid);

        Games.StatusChanged += (_, msg) =>
        {
            Notifier.Show(new Alert("game-mode", AlertSeverity.Info, "Modo jogo", msg, DateTimeOffset.Now));
            RaiseGameStatus();
        };
        Settings.Changed += OnSettingsChanged;
        Engine.PersistFailed += (_, ex) => AppLog.Error("Não foi possível guardar o registo de ajustes ativos (tenta de novo na próxima alteração)", ex);
    }

    public AppPaths Paths { get; }
    public SettingsService Settings { get; }
    public PlatformServices Platform { get; }
    public INotifier Notifier { get; }
    public IUiDispatcher Ui { get; }
    public TweakEngine Engine { get; }
    public GameSessionManager Games { get; }
    public AlertEngine Alerts { get; }
    public HealthService Health { get; }
    public ChangeGuard Guard { get; }
    public OptimizerService Optimizer { get; }
    public NetworkTester Network { get; }
    public BenchmarkService Benchmark { get; }
    public UpdateService? Updates { get; set; }

    public SensorSnapshot? LastSnapshot { get; private set; }
    public bool IsGameActive => Games.CurrentGame is not null;
    public IReadOnlyCollection<GameEvent> RunningGames => _running.Values.ToList();

    /// <summary>UI thread.</summary>
    public event EventHandler<SensorSnapshot>? SensorsUpdated;
    /// <summary>UI thread.</summary>
    public event EventHandler? GameStatusChanged;
    /// <summary>UI thread.</summary>
    public event EventHandler? RunningGamesChanged;

    public string GameStatusText
    {
        get
        {
            if (Games.CurrentGame is { } g) return $"Modo jogo ativo: {g.DisplayName ?? KnownGames.NameOf(g.ExecutableName) ?? g.ExecutableName}";
            return Settings.Current.AutoGameMode ? "A aguardar jogo…" : "Modo jogo automático desligado";
        }
    }

    /// <summary>Recovers from a previous crash, then starts the watchers. Safe to call once.</summary>
    public async Task StartAsync(bool syncAutostart, CancellationToken ct = default)
    {
        try
        {
            await Games.RecoverAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) { AppLog.Error("Falha na recuperação de sessão de jogo", ex); }

        if (syncAutostart) SyncAutostart(Settings.Current.StartWithWindows);

        Platform.GameWatcher.GameStarted += OnGameStarted;
        Platform.GameWatcher.GameStopped += OnGameStopped;
        try { Platform.GameWatcher.Start(); }
        catch (Exception ex) { AppLog.Error("Detetor de jogos não arrancou", ex); }

        _sensorTimer = new Timer(_ => PollSensors(), null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
    }

    private void PollSensors()
    {
        if (Interlocked.Exchange(ref _polling, 1) == 1) return; // previous read still running
        try
        {
            SensorSnapshot s;
            try { s = Platform.Sensors.Read(); }
            catch (Exception ex) { AppLog.Warn("Leitura de sensores falhou", ex); return; }

            RecordTemps(s);
            var alerts = Alerts.Evaluate(s);
            foreach (var a in alerts)
            {
                Notifier.Show(a);
                if (a.Severity == AlertSeverity.Critical && a.Key.EndsWith("-temp", StringComparison.Ordinal))
                    _ = AbortForHeatAsync();
            }
            Ui.Post(() =>
            {
                if (_disposed) return;
                LastSnapshot = s;
                SensorsUpdated?.Invoke(this, s);
            });
        }
        finally
        {
            Volatile.Write(ref _polling, 0);
        }
    }

    private async Task AbortForHeatAsync()
    {
        try
        {
            var wasActive = IsGameActive;
            await Games.AbortAsync().ConfigureAwait(false);
            if (wasActive)
                Notifier.Show(new Alert("heat-abort", AlertSeverity.Critical, "Ajustes de jogo pausados",
                    "Por segurança, o PC Max reverteu os ajustes de desempenho deste jogo. Voltam a ser aplicados da próxima vez que abrires um jogo.", DateTimeOffset.Now));
            RaiseGameStatus();
        }
        catch (Exception ex) { AppLog.Error("Falha ao pausar modo jogo por temperatura", ex); }
    }

    private void RecordTemps(SensorSnapshot s)
    {
        lock (_tempsGate)
        {
            _recentTemps.Enqueue((s.Timestamp, s.CpuTemperatureC, s.GpuTemperatureC));
            while (_recentTemps.Count > 0 && s.Timestamp - _recentTemps.Peek().At > TimeSpan.FromMinutes(10))
                _recentTemps.Dequeue();
        }
    }

    private (double? cpu, double? gpu) RecentMaxTemps()
    {
        lock (_tempsGate)
        {
            double? cpu = _recentTemps.Max(x => x.Cpu);
            double? gpu = _recentTemps.Max(x => x.Gpu);
            return (cpu, gpu);
        }
    }

    private async void OnGameStarted(object? sender, GameEvent e)
    {
        _running[e.ProcessId] = e;
        Ui.Post(() => RunningGamesChanged?.Invoke(this, EventArgs.Empty));
        try { await Games.OnGameStartedAsync(e).ConfigureAwait(false); }
        catch (Exception ex) { AppLog.Error($"Falha ao ativar modo jogo para {e.ExecutableName}", ex); }
        RaiseGameStatus();
    }

    private async void OnGameStopped(object? sender, GameEvent e)
    {
        _running.TryRemove(e.ProcessId, out _);
        Ui.Post(() => RunningGamesChanged?.Invoke(this, EventArgs.Empty));
        try { await Games.OnGameStoppedAsync(e).ConfigureAwait(false); }
        catch (Exception ex) { AppLog.Error($"Falha ao terminar modo jogo para {e.ExecutableName}", ex); }
        RaiseGameStatus();
    }

    private void RaiseGameStatus() => Ui.Post(() => { if (!_disposed) GameStatusChanged?.Invoke(this, EventArgs.Empty); });

    private void OnSettingsChanged(object? sender, SettingsChange c)
    {
        Engine.MaxAllowedRisk = c.New.MaxRisk;
        if (c.Old.Temperatures != c.New.Temperatures) Alerts.Thresholds = c.New.Temperatures;
        // schtasks.exe can take seconds: never on the UI thread (settings change on it).
        // Each run reads the latest value, so quick on/off toggles always end in the last state.
        if (c.Old.StartWithWindows != c.New.StartWithWindows)
            _ = Task.Run(() => SyncAutostart(Settings.Current.StartWithWindows));
        if (c.Old.AutoGameMode != c.New.AutoGameMode)
        {
            if (!c.New.AutoGameMode) _ = Task.Run(AbortQuietlyAsync);
            RaiseGameStatus();
        }
    }

    private async Task AbortQuietlyAsync()
    {
        try { await Games.AbortAsync().ConfigureAwait(false); }
        catch (Exception ex) { AppLog.Error("Falha ao desligar o modo jogo", ex); }
        RaiseGameStatus();
    }

    private readonly object _autostartGate = new();

    private void SyncAutostart(bool on)
    {
        lock (_autostartGate) SyncAutostartCore(on);
    }

    private void SyncAutostartCore(bool on)
    {
        try
        {
            if (on)
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe)) Platform.Autostart.Enable(exe);
            }
            else if (Platform.Autostart.IsEnabled())
            {
                Platform.Autostart.Disable();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Falha ao alterar o arranque com o Windows", ex);
            // InvalidOperationException carries a message written for the user (e.g. "not installed in Program Files").
            var msg = ex is InvalidOperationException ? ex.Message : "Não foi possível alterar o arranque automático. Tenta de novo nas Definições.";
            Notifier.Show(new Alert("autostart", AlertSeverity.Warning, "Arranque com o Windows", msg, DateTimeOffset.Now));
        }
    }

    /// <summary>
    /// Called before exit: undo the game session, and any game-session tweak still applied without one
    /// (e.g. kept by a benchmark), so nothing stays changed after the app closes.
    /// </summary>
    public async Task ShutdownAsync()
    {
        _sensorTimer?.Dispose();
        _sensorTimer = null;
        try { Platform.GameWatcher.Stop(); } catch { }
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await Games.AbortAsync(cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) { AppLog.Error("Falha ao reverter sessão de jogo ao sair", ex); }
    }

    /// <summary>Synchronous, time-capped version of <see cref="ShutdownAsync"/> for crash handlers and Windows logoff.</summary>
    public void EmergencyRevert(TimeSpan timeout)
    {
        try
        {
            _sensorTimer?.Dispose();
            try { Platform.GameWatcher.Stop(); } catch { }
            using var cts = new CancellationTokenSource(timeout);
            Task.Run(() => Games.AbortAsync(cts.Token)).Wait(timeout);
        }
        catch (Exception ex) { AppLog.Error("Reversão de emergência falhou", ex); }
        try { Settings.Flush(); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sensorTimer?.Dispose();
        Updates?.Dispose();
        try { Platform.Dispose(); } catch (Exception ex) { AppLog.Warn("Erro ao libertar plataforma", ex); }
    }
}

public static class AppHostFactory
{
    /// <summary>Builds the full runtime for this machine (real platform on Windows, fakes elsewhere).</summary>
    public static AppHost Create(AppPaths paths, SettingsService settings, INotifier notifier, IUiDispatcher ui)
    {
        // Leftover suspended apps are resumed inside PlatformFactory.Create (WindowsPlatform.Create).
        var platform = PlatformFactory.Create(paths, () => settings.Current);
        ISnapshotStore store = PlatformFactory.UseFake ? new InMemorySnapshotStore() : new JsonSnapshotStore(paths.SnapshotsFile);
        ILoadGenerator load = PlatformFactory.UseFake ? new FakeLoadGenerator() : new HttpDownloadLoad();
        return new AppHost(paths, settings, platform, notifier, ui, load, store);
    }
}
