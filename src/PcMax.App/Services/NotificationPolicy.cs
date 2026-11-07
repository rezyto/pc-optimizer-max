using PcMax.Core.Alerts;
using PcMax.Core.Settings;

namespace PcMax.App.Services;

public static class NotificationPolicy
{
    /// <summary>Notifications on/off, and only critical ones while a game runs (if "quiet while gaming").</summary>
    public static bool ShouldShow(AppSettings s, bool gameActive, AlertSeverity severity)
    {
        if (!s.NotificationsEnabled) return false;
        if (gameActive && s.QuietWhileGaming && severity != AlertSeverity.Critical) return false;
        return true;
    }
}
