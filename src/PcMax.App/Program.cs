using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using PcMax.App.Services;
using PcMax.Core.Settings;
using Velopack;

namespace PcMax.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
#pragma warning disable CA1416 // the hook only ever runs on Windows (Velopack installer)
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ =>
            {
                // Leave nothing behind: remove the "start with Windows" task.
                try { PlatformFactory.DisableAutostart(AppPaths.Default()); }
                catch (Exception ex) { AppLog.Error("Falha ao remover arranque automático na desinstalação", ex); }
            })
            .Run();
#pragma warning restore CA1416

        var paths = AppPaths.Default();
        AppLog.Init(paths);
        InstallCrashHandlers();

        using var instance = SingleInstance.Acquire();
        if (!instance.IsFirst)
        {
            SingleInstance.SignalExisting();
            return 0;
        }

        CultureInfo.DefaultThreadCurrentCulture = Format.Pt;
        CultureInfo.DefaultThreadCurrentUICulture = Format.Pt;

        App.Options = new StartupOptions(
            Minimized: args.Any(a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase)),
            Paths: paths,
            Instance: instance);

        try
        {
            AppLog.Info($"PC Max {Core.AppInfo.Version} a arrancar (minimizado: {App.Options.Minimized}).");
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
        }
        catch (Exception ex)
        {
            AppLog.Error("Erro fatal", ex);
            ShowFatal("O PC Max encontrou um erro e vai fechar. As alterações de jogo foram revertidas.\n\nDetalhes nos registos: " + paths.LogsDir);
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static void InstallCrashHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            AppLog.Error("Exceção não tratada (fatal)", e.ExceptionObject as Exception);
            App.EmergencyRevert();
            if (e.IsTerminating)
                ShowFatal("O PC Max encontrou um erro inesperado e vai fechar. Os ajustes de jogo foram revertidos.");
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLog.Error("Tarefa com erro não observado", e.Exception);
            e.SetObserved();
        };
    }

    /// <summary>Last-resort message when the UI itself is gone.</summary>
    private static void ShowFatal(string message)
    {
        try
        {
            if (OperatingSystem.IsWindows()) MessageBoxW(IntPtr.Zero, message, "PC Max", 0x10 /* MB_ICONERROR */);
            else Console.Error.WriteLine(message);
        }
        catch { }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}

public sealed record StartupOptions(bool Minimized = false, AppPaths? Paths = null, SingleInstance? Instance = null);
