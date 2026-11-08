using PcMax.Core.Alerts;
using PcMax.Core.Tweaks;

namespace PcMax.Core.Settings;

public sealed record AppSettings
{
    public const int MaxBackgroundApps = 100;
    public const int MaxGames = 200;

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

    // Background apps that the game mode may pause (by process name, without .exe). User-editable.
    // Game launchers are deliberately NOT here: some games (e.g. Fortnite/EOS) need them running.
    public IReadOnlyList<string> BackgroundAppsToSuspend { get; init; } = new[]
    {
        "OneDrive", "Teams", "ms-teams", "Spotify", "Dropbox", "GoogleDriveFS",
    };

    public IReadOnlyList<Games.GameProfile> Games { get; init; } = Array.Empty<Games.GameProfile>();

    /// <summary>
    /// Makes settings read from disk safe: every null becomes its default, lists are capped,
    /// process names must be plain names and protected processes are removed. Never throws.
    /// </summary>
    public AppSettings Normalize()
    {
        var t = (Temperatures ?? new TemperatureThresholds()).Normalize();
        var host = string.IsNullOrWhiteSpace(NetworkTestHost) || NetworkTestHost.Trim().Length > 253 ? "1.1.1.1" : NetworkTestHost.Trim();
        var games = (Games ?? Array.Empty<Games.GameProfile>())
            .Where(g => g is not null)
            .Select(g => g!.Normalize())
            .Where(g => g is not null)
            .Select(g => g!)
            .GroupBy(g => g.ExecutableName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(MaxGames)
            .ToList();
        return this with
        {
            Temperatures = t,
            NetworkTestHost = host,
            MaxRisk = Enum.IsDefined(MaxRisk) ? MaxRisk : RiskLevel.Safe,
            BackgroundAppsToSuspend = (BackgroundAppsToSuspend ?? Array.Empty<string>())
                .Select(ProcessNames.Clean)
                .Where(n => n is not null && !ProtectedProcesses.IsProtected(n))
                .Select(n => n!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxBackgroundApps)
                .ToList(),
            Games = games,
        };
    }
}
