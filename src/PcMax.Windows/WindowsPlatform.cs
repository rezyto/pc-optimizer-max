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
    /// <param name="paths">Per-user data folders (logs, suspended.json, power-scheme.json).</param>
    /// <param name="settings">Current settings (read on every use, so changes apply without rebuilding).</param>
    /// <summary>Removes the logon task without starting the rest of the platform (used on uninstall).</summary>
    public static void DisableAutostart()
    {
        try { new TaskSchedulerAutostart().Disable(); } catch (Exception ex) { Log.Warn("Falha ao remover o arranque automático: " + ex.Message); }
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
        var tweaks = new List<ITweak>
        {
            new PowerPlanTweak(paths),
            new TimerResolutionTweak(),
            new GameModeTweak(),
            new BackgroundRecordingTweak(),
            new MaxRefreshTweak(),
        };

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
