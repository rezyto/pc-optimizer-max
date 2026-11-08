using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using PcMax.App.Services;
using PcMax.App.ViewModels;
using PcMax.App.Views;
using PcMax.Core.Alerts;
using PcMax.Core.Settings;

namespace PcMax.App;

public partial class App : Application
{
    private static AppHost? _current;
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private AppHost? _host;
    private MainWindow? _main;
    private DialogService? _dialogs;
    private bool _exiting;
    private bool _trayHintShown;

    public static StartupOptions Options { get; set; } = new();

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Dispatcher.UIThread.UnhandledException += OnUiException;
            try
            {
                StartApp(desktop);
            }
            catch (Exception ex)
            {
                AppLog.Error("Falha ao arrancar", ex);
                throw;
            }
        }
        base.OnFrameworkInitializationCompleted();
    }

    private void StartApp(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var paths = Options.Paths ?? PlatformFactory.DefaultPaths();
        var settings = new SettingsService(new SettingsStore(paths.SettingsFile));
        var ui = new AvaloniaUiDispatcher();
        var toasts = new ToastNotifier(settings, () => _host?.IsGameActive ?? false, ShowMainWindow);

        _host = AppHostFactory.Create(paths, settings, toasts, ui);
        _current = _host;
        _host.Updates = new UpdateService(settings, toasts, GameRunning, paths) { ExitApp = ExitAsync };

        _dialogs = new DialogService(() => _main is { IsVisible: true } ? _main : null);
        var vm = new MainWindowViewModel(_host, _dialogs);
        _main = new MainWindow { DataContext = vm };
        _main.Closing += OnMainClosing;

        DataContext = new TrayViewModel(settings, ui, ShowMainWindow, RevertAllFromTrayAsync, ExitAsync);

        // Windows logoff/shutdown: undo the game session first (power plan would otherwise survive the reboot).
        desktop.ShutdownRequested += (_, _) =>
        {
            _host?.EmergencyRevert(TimeSpan.FromSeconds(5));
            Cleanup();
        };
        Options.Instance?.ListenForShow(() => Dispatcher.UIThread.Post(ShowMainWindow));

        var startMinimized = Options.Minimized && settings.Current.StartMinimizedToTray;
        if (!startMinimized)
        {
            _main.Show();
            vm.Dashboard.OnActivated();
        }

        _ = StartBackgroundAsync(vm, paths, firstRunUi: !startMinimized);
    }

    private async Task StartBackgroundAsync(MainWindowViewModel vm, AppPaths paths, bool firstRunUi)
    {
        var host = _host!;
        try
        {
            // An update downloaded in a previous run is installed now, before the game watcher starts.
            if (host.Updates is { } updates && await Task.Run(updates.TryInstallPendingAtStartup))
            {
                await ExitAsync();
                return;
            }
            // Only the installed copy (Program Files) registers the elevated logon task; dev builds never do.
            await Task.Run(() => host.StartAsync(syncAutostart: PlatformFactory.IsInstalledCopy));
            host.Updates?.Start();
        }
        catch (Exception ex)
        {
            AppLog.Error("Falha ao iniciar serviços em segundo plano", ex);
        }

        if (firstRunUi && FirstRun.IsFirstRun(paths) && _main is not null)
        {
            FirstRun.MarkDone(paths);
            var analyze = await new WelcomeWindow().ShowDialog<bool>(_main);
            if (analyze)
            {
                vm.Navigate(vm.Doctor);
                if (vm.Doctor.AnalyzeCommand.CanExecute(null)) vm.Doctor.AnalyzeCommand.Execute(null);
            }
        }
    }

    /// <summary>
    /// For the updater: a game session, or any detected game running (also with automatic game mode off),
    /// blocks checks, downloads and installs.
    /// </summary>
    private bool GameRunning() => _host is { } h && (h.IsGameActive || h.RunningGames.Count > 0);

    public void ShowMainWindow()
    {
        if (_main is null || _exiting) return;
        if (!_main.IsVisible) _main.Show();
        if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
        _main.Activate();
        (_main.DataContext as MainWindowViewModel)?.CurrentPage.OnActivated();
    }

    private void OnMainClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_exiting || _host is null) return;
        e.Cancel = true;
        if (_host.Settings.Current.CloseToTray)
        {
            _main!.Hide();
            if (!_trayHintShown)
            {
                _trayHintShown = true;
                _host.Notifier.Show(new Alert("tray-hint", AlertSeverity.Info, "O PC Max continua ativo",
                    "Está na área de notificação a vigiar temperaturas e jogos. Para sair, usa “Sair” no ícone.", DateTimeOffset.Now));
            }
        }
        else
        {
            _ = ExitAsync();
        }
    }

    private async Task RevertAllFromTrayAsync()
    {
        if (_host is null || _dialogs is null) return;
        var ok = await _dialogs.ConfirmAsync("Reverter tudo?",
            "Todos os ajustes ativos voltam ao estado em que estavam antes do PC Max.", "Reverter tudo", danger: true);
        if (!ok) return;
        try
        {
            var results = await Task.Run(() => _host.Engine.RevertAllAsync());
            var failed = results.Count(r => r.Action == Core.Tweaks.HistoryAction.Failed);
            _host.Notifier.Show(new Alert("revert-all", failed == 0 ? AlertSeverity.Info : AlertSeverity.Warning,
                "Reverter tudo", failed == 0 ? "Tudo revertido. O teu PC está como estava." : $"{failed} ajustes não foram revertidos. Vê o Histórico.",
                DateTimeOffset.Now));
        }
        catch (Exception ex)
        {
            AppLog.Error("Reverter tudo (bandeja) falhou", ex);
        }
    }

    public async Task ExitAsync()
    {
        if (_exiting) return;
        _exiting = true;
        var host = _host;
        if (host is not null)
        {
            try { await Task.Run(host.ShutdownAsync); } catch (Exception ex) { AppLog.Error("Erro ao sair", ex); }
        }
        Cleanup();
        _main?.Close();
        _desktop?.Shutdown();
    }

    private void Cleanup()
    {
        var host = _host;
        _host = null;
        if (host is null) return;
        try { host.Settings.Dispose(); } catch { }
        try { host.Dispose(); } catch { }
        Options.Instance?.Dispose();
        AppLog.Info("PC Max terminou.");
    }

    /// <summary>Called by the crash handler: undo the game session so nothing stays boosted/suspended.</summary>
    public static void EmergencyRevert()
    {
        var host = _current;
        if (host is null) return;
        try { host.EmergencyRevert(TimeSpan.FromSeconds(5)); } catch { }
    }

    private void OnUiException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error("Erro na interface", e.Exception);
        e.Handled = true; // keep running: a UI glitch must not take down game mode or leave tweaks behind
        try
        {
            _ = _dialogs?.ShowMessageAsync("Algo correu mal",
                "Aconteceu um erro inesperado, mas o PC Max continua a funcionar e nada ficou a meio.\n\n" +
                "Se voltar a acontecer, envia-nos o ficheiro de registo (Definições → Abrir pasta de registos).");
        }
        catch { }
    }
}
