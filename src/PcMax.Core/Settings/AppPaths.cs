namespace PcMax.Core.Settings;

/// <summary>
/// Where the app keeps its data. The app runs elevated, so its state must NOT live anywhere a normal
/// user (or malware running as that user) can write: it lives in <c>%ProgramData%\PcMax</c>, which the
/// Windows layer creates with an admin-only DACL and re-checks on every start
/// (see <c>PcMax.Windows.SystemServices.SecureDataFolder</c>). The per-user folder used by older versions is
/// only read once, for migration (<see cref="LegacyRoot"/>).
/// </summary>
public sealed class AppPaths
{
    public AppPaths(string root) => Root = root;

    /// <summary>%ProgramData%\PcMax (resolved from HKLM, so a normal user cannot redirect it).</summary>
    public static AppPaths Default() =>
        new(System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolderOption.DoNotVerify),
            "PcMax"));

    /// <summary>Data folder of versions up to 0.1.x (per user, user-writable). Only read for the one-time migration.</summary>
    public static string LegacyRoot() =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), "PcMax");

    public string Root { get; }
    public string SettingsFile => System.IO.Path.Combine(Root, "settings.json");
    public string SnapshotsFile => System.IO.Path.Combine(Root, "active-tweaks.json");
    public string CrashGuardFile => System.IO.Path.Combine(Root, "crash-guard.json");
    public string LogsDir => System.IO.Path.Combine(Root, "logs");
    /// <summary>Downloaded installers (admin-only, like the rest of <see cref="Root"/>).</summary>
    public string UpdatesDir => System.IO.Path.Combine(Root, "updates");
}
