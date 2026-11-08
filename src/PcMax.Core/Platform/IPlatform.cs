using PcMax.Core.Doctor;

namespace PcMax.Core.Platform;

public sealed record StartupItem(string Id, string Name, string Command, string Location, bool Enabled, string? Publisher);

public interface IStartupManager
{
    IReadOnlyList<StartupItem> List();
    /// <summary>Disables/enables the item the same way Task Manager does (reversible, nothing deleted).</summary>
    void SetEnabled(string id, bool enabled);
}

public sealed record CleanupCategory(string Id, string Name, string Description, long Bytes, int Files, bool SafeByDefault);

public interface ICleaner
{
    Task<IReadOnlyList<CleanupCategory>> ScanAsync(CancellationToken ct = default);
    /// <summary>Deletes the selected categories. Files in use are skipped. Returns bytes freed.</summary>
    Task<long> CleanAsync(IEnumerable<string> categoryIds, CancellationToken ct = default);
}

public interface IHardwareInspector
{
    Task<HardwareProfile> InspectAsync(CancellationToken ct = default);
}

public interface IAutostart
{
    bool IsEnabled();
    /// <summary>
    /// Registers the app to start elevated at logon without a UAC prompt (Task Scheduler, highest privileges).
    /// Refuses (throws) when the executable's folder could be modified by a non-administrator.
    /// </summary>
    void Enable(string exePath);
    void Disable();
}

public interface IRestorePoints
{
    /// <summary>Creates a Windows System Restore point. Returns false if System Restore is off or rate-limited.</summary>
    Task<bool> CreateAsync(string description, CancellationToken ct = default);
}

public interface IProcessBooster
{
    /// <summary>Raises the game's priority. Returns the previous priority so it can be restored.</summary>
    string? BoostGame(int processId);
    void RestoreGame(int processId, string? previousPriority);
    /// <summary>
    /// Suspends background apps by name (never protected, elevated or system processes). Returns the PIDs suspended.
    /// <paramref name="excludePid"/> (the game) and every process in its parent chain are never suspended.
    /// </summary>
    IReadOnlyList<int> SuspendBackground(IEnumerable<string> processNames, int? excludePid = null);
    void ResumeAll(IEnumerable<int> processIds);
    /// <summary>
    /// True while <paramref name="processId"/> is still alive and still runs <paramref name="executableName"/>
    /// (so a reused pid does not count). Implementations that cannot tell return true.
    /// </summary>
    bool IsRunning(int processId, string executableName) => true;
}

/// <summary>Shows a pop-up notification to the user.</summary>
public interface INotifier
{
    void Show(Alerts.Alert alert);
}
