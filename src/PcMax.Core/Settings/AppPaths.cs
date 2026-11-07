namespace PcMax.Core.Settings;

/// <summary>Where the app keeps its data (per user). The autostart task runs as the same user,
/// so it reads the same files.</summary>
public sealed class AppPaths
{
    public AppPaths(string root) => Root = root;

    public static AppPaths Default() =>
        new(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PcMax"));

    public string Root { get; }
    public string SettingsFile => System.IO.Path.Combine(Root, "settings.json");
    public string SnapshotsFile => System.IO.Path.Combine(Root, "active-tweaks.json");
    public string CrashGuardFile => System.IO.Path.Combine(Root, "crash-guard.json");
    public string LogsDir => System.IO.Path.Combine(Root, "logs");
}
