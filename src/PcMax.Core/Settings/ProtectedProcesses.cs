namespace PcMax.Core.Settings;

/// <summary>Processes the app must never close, suspend or lower in priority.</summary>
public static class ProtectedProcesses
{
    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso",
        "svchost", "dwm", "explorer", "fontdrvhost", "sihost", "ctfmon", "conhost", "audiodg", "spoolsv",
        "MsMpEng", "NisSrv", "SecurityHealthService", "SecurityHealthSystray", "MpDefenderCoreService",
        "WmiPrvSE", "taskhostw", "RuntimeBroker", "StartMenuExperienceHost", "ShellExperienceHost",
        "SearchHost", "TextInputHost", "LogonUI", "userinit", "dllhost", "WUDFHost", "Memory Compression",
        // anti-cheat services: touching them can get a player banned or break the game
        "vgc", "vgtray", "EasyAntiCheat", "EasyAntiCheat_EOS", "BEService", "BEService_x64", "FACEIT", "faceitservice",
        // ourselves
        "PcMax",
    };

    public static bool IsProtected(string processName)
    {
        var n = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? processName[..^4] : processName;
        return Names.Contains(n);
    }
}
