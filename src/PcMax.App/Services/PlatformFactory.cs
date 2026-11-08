using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using PcMax.App.Services.Fakes;
using PcMax.Core.Platform;
using PcMax.Core.Settings;

namespace PcMax.App.Services;

/// <summary>
/// The ONLY place that touches PcMax.Windows. Every call is guarded by <see cref="OperatingSystem.IsWindows"/>
/// and kept in a non-inlined method so the Windows assembly is only loaded on Windows.
/// Elsewhere the app runs on <see cref="FakePlatform"/> for design and screenshots. On Windows, PCMAX_FAKE=1 does the
/// same, but only in DEBUG builds: a release build always runs the real platform, whatever the environment says.
/// </summary>
public static class PlatformFactory
{
    public static bool UseFake =>
        !OperatingSystem.IsWindows()
#if DEBUG
        || Environment.GetEnvironmentVariable("PCMAX_FAKE") == "1"
#endif
        ;

    /// <summary>Where state lives: %ProgramData%\PcMax on Windows; a per-user dev folder with the fake platform.</summary>
    public static AppPaths DefaultPaths() =>
        UseFake
            ? new AppPaths(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), "PcMax-dev"))
            : AppPaths.Default();

    /// <summary>
    /// Locks down the data folder (admin-only ACL) and migrates the old per-user state. Must run before anything
    /// reads state. Returns notes to log. Throws when the folder cannot be made safe.
    /// </summary>
    public static IReadOnlyList<string> EnsureDataFolder(AppPaths paths)
    {
        if (UseFake || !OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(paths.Root);
            return Array.Empty<string>();
        }
        return EnsureDataFolderWindows(paths);
    }

    /// <summary>The uninstaller's clean-up: resume suspended apps, revert every tweak, remove the logon task. Never throws.</summary>
    public static int UninstallCleanup(AppPaths paths)
    {
        if (!OperatingSystem.IsWindows()) return 0;
        return UninstallCleanupWindows(paths);
    }

    /// <summary>True for the copy installed by the setup in %ProgramFiles% (admin-only). False for dev builds.</summary>
    public static bool IsInstalledCopy => !UseFake && OperatingSystem.IsWindows() && IsInstalledCopyWindows();

    /// <summary>
    /// Signature policy for a downloaded installer: null when acceptable, else the reason. When the running exe is
    /// Authenticode-signed, the installer must have a valid signature from the same certificate.
    /// </summary>
    public static string? CheckUpdateSignature(string installer)
    {
        if (!OperatingSystem.IsWindows()) return "atualizações só no Windows";
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return "caminho da app desconhecido";
        return CheckUpdateSignatureWindows(installer, exe);
    }

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

    [MethodImpl(MethodImplOptions.NoInlining), SupportedOSPlatform("windows")]
    private static IReadOnlyList<string> EnsureDataFolderWindows(AppPaths paths) =>
        PcMax.Windows.SystemServices.SecureDataFolder.Ensure(paths);

    [MethodImpl(MethodImplOptions.NoInlining), SupportedOSPlatform("windows")]
    private static int UninstallCleanupWindows(AppPaths paths) =>
        PcMax.Windows.WindowsPlatform.UninstallCleanup(paths);

    [MethodImpl(MethodImplOptions.NoInlining), SupportedOSPlatform("windows")]
    private static bool IsInstalledCopyWindows() =>
        PcMax.Windows.SystemServices.InstallLocation.IsInstalledCopy();

    [MethodImpl(MethodImplOptions.NoInlining), SupportedOSPlatform("windows")]
    private static string? CheckUpdateSignatureWindows(string installer, string exe) =>
        PcMax.Windows.SystemServices.Authenticode.CheckUpdateSignature(installer, exe);

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
