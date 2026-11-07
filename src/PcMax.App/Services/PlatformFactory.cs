using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using PcMax.App.Services.Fakes;
using PcMax.Core.Platform;
using PcMax.Core.Settings;

namespace PcMax.App.Services;

/// <summary>
/// The ONLY place that touches PcMax.Windows. Every call is guarded by <see cref="OperatingSystem.IsWindows"/>
/// and kept in a non-inlined method so the Windows assembly is only loaded on Windows.
/// Elsewhere (or with PCMAX_FAKE=1) the app runs on <see cref="FakePlatform"/> for design and screenshots.
/// </summary>
public static class PlatformFactory
{
    public static bool UseFake =>
        !OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("PCMAX_FAKE") == "1";

    public static PlatformServices Create(AppPaths paths, Func<AppSettings> settings)
    {
        if (!UseFake && OperatingSystem.IsWindows())
            return CreateWindows(paths, settings);
        return FakePlatform.Create();
    }

    /// <summary>Resumes background apps left suspended by a previous run that crashed mid-game.</summary>
    public static void ResumeLeftovers(AppPaths paths)
    {
        if (!UseFake && OperatingSystem.IsWindows())
            ResumeLeftoversWindows(paths);
    }

    /// <summary>Used by the Velopack uninstall hook: remove the logon task.</summary>
    public static void DisableAutostart(AppPaths paths)
    {
        if (!OperatingSystem.IsWindows()) return;
        PcMax.Windows.WindowsPlatform.DisableAutostart();
    }

    /// <summary>Points the platform log at our logs folder (before anything is logged).</summary>
    internal static void InitLog(AppPaths paths)
    {
        if (!UseFake && OperatingSystem.IsWindows()) InitLogWindows(paths);
    }

    /// <summary>Returns true when the message went to the platform log.</summary>
    internal static bool ForwardLog(string level, string message, Exception? ex)
    {
        if (UseFake || !OperatingSystem.IsWindows()) return false;
        ForwardLogWindows(level, message, ex);
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining), SupportedOSPlatform("windows")]
    private static PlatformServices CreateWindows(AppPaths paths, Func<AppSettings> settings) =>
        PcMax.Windows.WindowsPlatform.Create(paths, settings);

    [MethodImpl(MethodImplOptions.NoInlining), SupportedOSPlatform("windows")]
    private static void ResumeLeftoversWindows(AppPaths paths) =>
        PcMax.Windows.ProcessBooster.ResumeLeftovers(paths);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InitLogWindows(AppPaths paths) => PcMax.Windows.Log.Init(paths.LogsDir);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ForwardLogWindows(string level, string message, Exception? ex)
    {
        switch (level)
        {
            case "ERROR": PcMax.Windows.Log.Error(message, ex); break;
            case "WARN": PcMax.Windows.Log.Warn(message, ex); break;
            default: PcMax.Windows.Log.Info(ex is null ? message : message + " | " + ex); break;
        }
    }
}
