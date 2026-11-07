using PcMax.Core.Alerts;
using PcMax.Core.Tweaks;

namespace PcMax.Core.Settings;

public sealed record AppSettings
{
    public int SchemaVersion { get; init; } = 1;

    // Startup and behaviour
    public bool StartWithWindows { get; init; } = true;
    public bool StartMinimizedToTray { get; init; } = true;
    public bool CloseToTray { get; init; } = true;
    public bool AutoGameMode { get; init; } = true;
    public RiskLevel MaxRisk { get; init; } = RiskLevel.Safe;
    public bool CreateRestorePointBeforeChanges { get; init; } = true;

    // Notifications
    public bool NotificationsEnabled { get; init; } = true;
    public bool QuietWhileGaming { get; init; } = true; // only critical alerts during games
    public TemperatureThresholds Temperatures { get; init; } = new();

    // Updates
    public bool AutoUpdate { get; init; } = true;

    // Network
    public string NetworkTestHost { get; init; } = "1.1.1.1";

    // Privacy: nothing leaves the PC unless this is true. Off by default.
    public bool ShareAnonymousStats { get; init; } = false;

    // Background apps that the game mode may close (by process name, without .exe). User-editable.
    public IReadOnlyList<string> BackgroundAppsToSuspend { get; init; } = new[]
    {
        "OneDrive", "Teams", "ms-teams", "Spotify", "EpicGamesLauncher", "Dropbox", "GoogleDriveFS",
    };

    public IReadOnlyList<Games.GameProfile> Games { get; init; } = Array.Empty<Games.GameProfile>();

    public AppSettings Normalize()
    {
        var t = Temperatures.Normalize();
        var host = string.IsNullOrWhiteSpace(NetworkTestHost) || NetworkTestHost.Length > 253 ? "1.1.1.1" : NetworkTestHost.Trim();
        return this with
        {
            Temperatures = t,
            NetworkTestHost = host,
            MaxRisk = Enum.IsDefined(MaxRisk) ? MaxRisk : RiskLevel.Safe,
            BackgroundAppsToSuspend = (BackgroundAppsToSuspend ?? Array.Empty<string>())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .Where(n => !ProtectedProcesses.IsProtected(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Games = Games ?? Array.Empty<Games.GameProfile>(),
        };
    }
}
