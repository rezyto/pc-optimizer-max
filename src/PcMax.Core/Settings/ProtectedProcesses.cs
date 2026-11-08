namespace PcMax.Core.Settings;

/// <summary>Processes the app must never close, suspend or lower in priority.</summary>
public static class ProtectedProcesses
{
    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        // Windows core
        "System", "Idle", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso", "lsm",
        "svchost", "dwm", "explorer", "fontdrvhost", "sihost", "ctfmon", "conhost", "audiodg", "spoolsv",
        "WmiPrvSE", "taskhostw", "RuntimeBroker", "StartMenuExperienceHost", "ShellExperienceHost",
        "SearchHost", "SearchIndexer", "TextInputHost", "LogonUI", "userinit", "dllhost", "WUDFHost", "Memory Compression",
        "consent", "smartscreen", "SgrmBroker", "Secure System", "vmmem", "vmcompute",
        // tools the user needs to stop a misbehaving program
        "Taskmgr", "mmc", "regedit", "cmd", "powershell", "pwsh", "WindowsTerminal", "OpenConsole",
        // Windows security / Defender / Defender for Endpoint
        "MsMpEng", "NisSrv", "SecurityHealthService", "SecurityHealthSystray", "SecHealthUI", "MpDefenderCoreService",
        "MsSense", "SenseIR", "SenseCncProxy", "SenseNdr", "SenseTVM", "MpCmdRun",
        // anti-cheat: touching them can get a player banned or break the game
        "vgc", "vgtray", "vgk",
        "EasyAntiCheat", "EasyAntiCheat_EOS", "EasyAntiCheat_Setup",
        "BEService", "BEService_x64", "BattlEye", "BELauncher",
        "FACEIT", "faceitservice", "FaceitClient",
        "EAAntiCheat.GameService", "EAAntiCheat.GameServiceLauncher", "EAAntiCheat.Installer",
        "PnkBstrA", "PnkBstrB",
        "SGuard64", "SGuardSvc64", "ACE-Tray", "ACE-Guard Client",
        "GameMon", "GameMon64", "GameGuard",
        "xigncode", "x3", "ESEADriver2", "zksvc",
        "Ricochet", "atvi-randgrid_sr", "RandgridService",
        // launchers and platform clients that games (and their DRM / anti-cheat) depend on
        "steam", "steamwebhelper", "steamservice", "EpicGamesLauncher", "EpicWebHelper", "EOSOverlayRenderer-Win64-Shipping",
        "Battle.net", "Agent", "EADesktop", "EABackgroundService", "EALocalHostSvc",
        "RiotClientServices", "RiotClientUx", "RiotClientCrashHandler", "upc", "UplayWebCore", "UbisoftConnect",
        "GalaxyClient", "XboxPcApp", "GamingServices", "GamingServicesNet",
        // ourselves
        "PcMax",
    };

    public static bool IsProtected(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return true;
        var n = processName.Trim();
        n = n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n[..^4] : n;
        return Names.Contains(n);
    }
}
