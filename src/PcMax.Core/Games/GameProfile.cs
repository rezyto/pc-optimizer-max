namespace PcMax.Core.Games;

/// <summary>Per-game settings applied automatically when the game starts and undone when it closes.</summary>
public sealed record GameProfile
{
    public string ExecutableName { get; init; } = "";   // e.g. "cs2.exe"
    public string DisplayName { get; init; } = "";
    public bool Enabled { get; init; } = true;
    public bool HighPriority { get; init; } = true;
    public bool SuspendBackgroundApps { get; init; } = true;
    public IReadOnlyList<string> TweakIds { get; init; } = new[] { "power.high-performance", "latency.timer-resolution" };
}

public sealed record GameEvent(string ExecutableName, int ProcessId, string? DisplayName);

/// <summary>Watches for games starting and stopping.</summary>
public interface IGameWatcher : IDisposable
{
    event EventHandler<GameEvent>? GameStarted;
    event EventHandler<GameEvent>? GameStopped;
    void Start();
    void Stop();
}

/// <summary>Built-in list of popular games so they are detected with no setup.</summary>
public static class KnownGames
{
    public static readonly IReadOnlyDictionary<string, string> All = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["cs2.exe"] = "Counter-Strike 2",
        ["VALORANT-Win64-Shipping.exe"] = "Valorant",
        ["FortniteClient-Win64-Shipping.exe"] = "Fortnite",
        ["League of Legends.exe"] = "League of Legends",
        ["r5apex.exe"] = "Apex Legends",
        ["r5apex_dx12.exe"] = "Apex Legends",
        ["cod.exe"] = "Call of Duty",
        ["GTA5.exe"] = "GTA V",
        ["GTA5_Enhanced.exe"] = "GTA V Enhanced",
        ["RocketLeague.exe"] = "Rocket League",
        ["Overwatch.exe"] = "Overwatch 2",
        ["dota2.exe"] = "Dota 2",
        ["RobloxPlayerBeta.exe"] = "Roblox",
        ["javaw.exe"] = "Minecraft (Java)",
        ["Minecraft.Windows.exe"] = "Minecraft (Bedrock)",
        ["FC25.exe"] = "EA Sports FC 25",
        ["FC26.exe"] = "EA Sports FC 26",
        ["eldenring.exe"] = "Elden Ring",
        ["Cyberpunk2077.exe"] = "Cyberpunk 2077",
        ["RainbowSix.exe"] = "Rainbow Six Siege",
        ["TslGame.exe"] = "PUBG",
        ["destiny2.exe"] = "Destiny 2",
        ["Marvel-Win64-Shipping.exe"] = "Marvel Rivals",
        ["deadlock.exe"] = "Deadlock",
        ["bf6.exe"] = "Battlefield 6",
    };

    public static string? NameOf(string exe) => All.GetValueOrDefault(exe);
}
