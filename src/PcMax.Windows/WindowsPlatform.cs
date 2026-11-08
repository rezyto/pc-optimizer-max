using System.Diagnostics;
using System.Runtime.Versioning;
using PcMax.Core.Platform;
using PcMax.Core.Settings;
using PcMax.Core.Tweaks;
using PcMax.Windows.Benchmark;
using PcMax.Windows.Cleaning;
using PcMax.Windows.Games;
using PcMax.Windows.Hardware;
using PcMax.Windows.Network;
using PcMax.Windows.Sensors;
using PcMax.Windows.Startup;
using PcMax.Windows.SystemServices;
using PcMax.Windows.Tweaks;

namespace PcMax.Windows;

/// <summary>Builds every Windows implementation of the platform services.</summary>
[SupportedOSPlatform("windows")]
public static class WindowsPlatform
{
    /// <summary>Removes the logon task without starting the rest of the platform.</summary>
    public static void DisableAutostart()
    {
        try { new TaskSchedulerAutostart().Disable(); } catch (Exception ex) { Log.Warn("Falha ao remover o arranque automático: " + ex.Message); }
    }

    /// <summary>Every Windows tweak, in a fixed order. Shared by the app and the uninstall clean-up.</summary>
    internal static List<ITweak> CreateTweaks(AppPaths paths) => new()
    {
        new PowerPlanTweak(paths),
        new TimerResolutionTweak(),
        new GameModeTweak(),
        new BackgroundRecordingTweak(),
        new MaxRefreshTweak(),
    };

    /// <summary>
    /// Run by the uninstaller (<c>PcMax.exe --uninstall-cleanup</c>, elevated, before the files are removed):
    /// wake anything left suspended, undo every tweak still recorded, remove the logon task. Never throws;
    /// every step is logged and the next one still runs.
    /// </summary>
    public static int UninstallCleanup(AppPaths paths)
    {
        try
        {
            try
            {
                foreach (var note in SecureDataFolder.Ensure(paths)) Log.Info(note);
            }
            catch (Exception ex) { Debug.WriteLine("PcMax: data folder not secure: " + ex.Message); }
            Log.Init(paths.LogsDir);
            Log.Info($"Desinstalação: limpeza a começar (PC Max {Core.AppInfo.Version}).");

            try { ProcessBooster.ResumeLeftovers(paths); }
            catch (Exception ex) { Log.Error("Desinstalação: retomar processos falhou", ex); }

            try
            {
                var engine = new TweakEngine(CreateTweaks(paths), new JsonSnapshotStore(paths.SnapshotsFile));
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                var revert = Task.Run(() => engine.RevertAllAsync(null, cts.Token));
                if (!revert.Wait(TimeSpan.FromSeconds(20)))
                {
                    cts.Cancel();
                    Log.Warn("Desinstalação: reverter ajustes demorou mais de 20 s; a continuar.");
                }
                else
                {
                    foreach (var r in revert.Result)
                        Log.Info($"Desinstalação: {r.TweakId} -> {r.Action} {r.Detail}");
                }
            }
            catch (Exception ex) { Log.Error("Desinstalação: reverter ajustes falhou", ex); }

            try { new TaskSchedulerAutostart().Disable(); }
            catch (Exception ex) { Log.Error("Desinstalação: remover tarefa de arranque falhou", ex); }

            Log.Info("Desinstalação: limpeza concluída.");
        }
        catch (Exception ex)
        {
            try { Log.Error("Desinstalação: erro inesperado", ex); } catch { }
        }
        return 0;
    }

    public static PlatformServices Create(AppPaths paths, Func<AppSettings> settings)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(settings);
        try { Directory.CreateDirectory(paths.Root); } catch { }
        Log.Init(paths.LogsDir);
        Log.Info($"PC Max a iniciar (Windows {Environment.OSVersion.Version}).");

        // Wake up anything a previous run left suspended (crash, power cut).
        ProcessBooster.ResumeLeftovers(paths);

        var startup = new StartupManager();
        var tweaks = CreateTweaks(paths);

        return new PlatformServices(
            Sensors: new LhmSensorProvider(),
            Hardware: new HardwareInspector(paths, startup),
            Tweaks: tweaks,
            GameWatcher: new ProcessGameWatcher(settings),
            Booster: new ProcessBooster(paths),
            Startup: startup,
            Cleaner: new Cleaner(),
            Autostart: new TaskSchedulerAutostart(),
            RestorePoints: new SystemRestorePoints(),
            Network: new PingProbe(),
            FrameCapture: new PresentMonCapture());
    }
}
