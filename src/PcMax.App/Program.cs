using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using PcMax.App.Services;
using PcMax.Core.Settings;

// Our own P/Invokes only target system DLLs: resolve them from System32, never from the app or current folder.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace PcMax.App;

public static class Program
{
    private const uint LOAD_LIBRARY_SEARCH_APPLICATION_DIR = 0x00000200;
    private const uint LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x00000800;

    [STAThread]
    public static int Main(string[] args)
    {
        // Before anything loads a DLL: no current directory or PATH in the DLL search order for this process.
        HardenDllSearch();

        // The uninstaller runs "PcMax.exe --uninstall-cleanup" before removing the files: no UI, no single instance.
        if (args.Any(a => string.Equals(a, "--uninstall-cleanup", StringComparison.OrdinalIgnoreCase)))
            return UninstallCleanup();

        using var instance = SingleInstance.Acquire();
        if (!instance.IsFirst)
        {
            SingleInstance.SignalExisting();
            return 0;
        }

        // The data folder must be admin-only before any state is read (settings, snapshots, suspended apps).
        var paths = PlatformFactory.DefaultPaths();
        IReadOnlyList<string> folderNotes;
        try
        {
            folderNotes = PlatformFactory.EnsureDataFolder(paths);
        }
        catch (Exception ex)
        {
            ShowFatal("O PC Max não conseguiu proteger a sua pasta de dados e não vai arrancar, por segurança.\n\n" +
                      $"Pasta: {paths.Root}\nDetalhes: {ex.Message}");
            return 1;
        }

        AppLog.Init(paths);
        foreach (var note in folderNotes) AppLog.Info(note);
        InstallCrashHandlers();

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

    private static int UninstallCleanup()
    {
        try
        {
            var paths = PlatformFactory.DefaultPaths();
            return PlatformFactory.UninstallCleanup(paths);
        }
        catch (Exception ex)
        {
            // Never block or fail the uninstall.
            try { Console.Error.WriteLine("PC Max uninstall clean-up: " + ex.Message); } catch { }
            return 0;
        }
    }

    private static void HardenDllSearch()
    {
        if (!OperatingSystem.IsWindows()) return;
        try { SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32 | LOAD_LIBRARY_SEARCH_APPLICATION_DIR); }
        catch { /* Windows 8+ always has it; never fail start-up over it */ }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDefaultDllDirectories(uint directoryFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}

public sealed record StartupOptions(bool Minimized = false, AppPaths? Paths = null, SingleInstance? Instance = null);
