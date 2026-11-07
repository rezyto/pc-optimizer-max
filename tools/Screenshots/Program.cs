using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PcMax.App;
using PcMax.App.Services;
using PcMax.App.Services.Fakes;
using PcMax.App.ViewModels;
using PcMax.App.Views;
using PcMax.Core.Alerts;
using PcMax.Core.Games;
using PcMax.Core.Platform;
using PcMax.Core.Settings;

namespace PcMax.Screenshots;

/// <summary>
/// Usage: dotnet run --project tools/Screenshots -- [outputDir]
/// Renders each page at 1280x800 (plus a tall "-completo" version) using the fake platform.
/// </summary>
public static class Program
{
    private sealed class RecordingNotifier : INotifier
    {
        public List<Alert> Shown { get; } = new();
        public void Show(Alert alert) { lock (Shown) Shown.Add(alert); }
    }

    public static int Main(string[] args)
    {
        var outDir = args.Length > 0 ? args[0] : Path.Combine(Environment.CurrentDirectory, "screens");
        Directory.CreateDirectory(outDir);

        AppBuilder.Configure<PcMax.App.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont()
            .SetupWithoutStarting();

        var root = Path.Combine(Path.GetTempPath(), "pcmax-screens-" + Guid.NewGuid().ToString("N")[..8]);
        var paths = new AppPaths(root);
        AppLog.Init(paths);
        var settings = new SettingsService(new SettingsStore(paths.SettingsFile));
        var notifier = new RecordingNotifier();
        var host = AppHostFactory.Create(paths, settings, notifier, new AvaloniaUiDispatcher());
        host.Updates = new UpdateService(settings, notifier, () => host.IsGameActive);
        BenchmarkService.CaptureDuration = TimeSpan.FromSeconds(1);
        Wait(host.StartAsync(syncAutostart: false));

        var vm = new MainWindowViewModel(host, dialogs: null);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 800 };
        window.Show();

        // 60 s of sensor history for the sparklines.
        for (var i = 0; i < 60; i++) vm.Dashboard.ApplySensors(host.Platform.Sensors.Read());
        Wait(host.Health.AnalyzeAsync());
        Pump(300);
        Shot(window, outDir, "01-painel");

        vm.Navigate(vm.Doctor);
        Pump(200);
        var exp = window.GetVisualDescendants().OfType<Expander>().FirstOrDefault(e => e.IsEffectivelyVisible);
        if (exp is not null) exp.IsExpanded = true;
        Pump(300);
        Shot(window, outDir, "02-medico", tall: 2000);

        // Dashboard after "Otimizar agora".
        vm.Navigate(vm.Dashboard);
        Wait(vm.Dashboard.OptimizeCommand.ExecuteAsync(null));
        var watcher = (FakeGameWatcher)host.Platform.GameWatcher;
        watcher.SimulateStart(new GameEvent("cs2.exe", 4242, "Counter-Strike 2"));
        Pump(1500);
        Shot(window, outDir, "03-painel-otimizado");


        vm.Navigate(vm.Games);
        var profile = vm.Games.AddProfile("cs2.exe");
        vm.Games.AddProfile("VALORANT-Win64-Shipping.exe");
        Pump(300);
        var tweak = profile.Tweaks.First();
        Wait(tweak.TestCommand.ExecuteAsync(null));
        Pump(300);
        Shot(window, outDir, "04-jogos", tall: 1300);

        vm.Navigate(vm.Network);
        Pump(100);
        Wait(vm.Network.TestCommand.ExecuteAsync(null));
        Wait(vm.Network.BufferbloatCommand.ExecuteAsync(null));
        Pump(300);
        Shot(window, outDir, "05-rede", tall: 1100);

        vm.Navigate(vm.Cleanup);
        Pump(300);
        Wait(vm.Cleanup.ScanCommand.ExecuteAsync(null));
        Pump(300);
        Shot(window, outDir, "06-limpeza", tall: 1400);

        vm.Navigate(vm.History);
        Pump(300);
        Shot(window, outDir, "07-historico", tall: 1200);

        vm.Navigate(vm.Settings);
        Pump(300);
        Shot(window, outDir, "08-definicoes", tall: 1250);

        // Smallest allowed window.
        vm.Navigate(vm.Dashboard);
        window.Width = 1000; window.Height = 650;
        Pump(300);
        Save(window, outDir, "12-painel-tamanho-minimo");
        window.Width = 1280; window.Height = 800;

        var welcome = new WelcomeWindow();
        welcome.Show();
        Pump(300);
        Save(welcome, outDir, "09-boas-vindas");
        welcome.Close();

        var confirm = new ConfirmDialog("Reverter tudo?",
            "Todos os ajustes ativos voltam ao estado em que estavam antes do PC Max. Podes voltar a otimizar quando quiseres.",
            "Reverter tudo", "Cancelar", danger: true);
        confirm.Show();
        Pump(300);
        Save(confirm, outDir, "10-confirmacao");
        confirm.Close();

        var toasts = new[]
        {
            new Alert("cpu-temp", AlertSeverity.Critical, "CPU demasiado quente!",
                "CPU está a 96 °C (limite crítico 95 °C). Guarda o jogo e verifica a ventilação. Os ajustes de desempenho foram pausados.", DateTimeOffset.Now),
            new Alert("gpu-temp", AlertSeverity.Warning, "Placa gráfica a aquecer",
                "Placa gráfica está a 85 °C (aviso a partir de 83 °C). Verifica ventoinhas e pó.", DateTimeOffset.Now),
            new Alert("update-ready", AlertSeverity.Info, "Atualização pronta — aplica ao reiniciar",
                "A versão 0.2.0 do PC Max foi transferida e instala-se quando fechares a app.", DateTimeOffset.Now),
        };
        var n = 0;
        foreach (var a in toasts)
        {
            var t = new ToastWindow { DataContext = new ToastViewModel(a) };
            t.Show();
            Pump(200);
            Save(t, outDir, $"11-notificacao-{++n}");
            t.Close();
        }

        Wait(host.ShutdownAsync());
        settings.Dispose();
        host.Dispose();
        Console.WriteLine("Notificações registadas: " + notifier.Shown.Count);
        try { Directory.Delete(root, true); } catch { }
        return 0;
    }

    private static void Shot(Window w, string dir, string name, int tall = 0)
    {
        w.Width = 1280; w.Height = 800;
        Pump(250);
        Save(w, dir, name);
        if (tall > 800)
        {
            w.Height = tall;
            Pump(250);
            Save(w, dir, name + "-completo");
            w.Height = 800;
            Pump(100);
        }
    }

    private static void Save(TopLevel w, string dir, string name)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var frame = w.CaptureRenderedFrame();
        if (frame is null) { Console.WriteLine("Sem imagem: " + name); return; }
        var path = Path.Combine(dir, name + ".png");
        frame.Save(path);
        Console.WriteLine(path);
    }

    private static void Pump(int ms)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        do
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Thread.Sleep(15);
        } while (DateTime.UtcNow < until);
    }

    private static void Wait(Task t)
    {
        var limit = DateTime.UtcNow.AddSeconds(60);
        while (!t.IsCompleted && DateTime.UtcNow < limit)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(15);
        }
        if (t.IsFaulted) throw t.Exception!;
    }
}
