namespace PcMax.Core.Tweaks;

/// <summary>How risky a change is. The user chooses the highest level the app may use.</summary>
public enum RiskLevel
{
    /// <summary>Cannot harm the PC. Fully reversible.</summary>
    Safe = 0,
    /// <summary>Changes hardware behaviour (voltages, limits). Reversible, needs confirmation.</summary>
    Advanced = 1,
    /// <summary>Pushes hardware past defaults. Desktop only, warranty warning.</summary>
    Extreme = 2,
}

public enum TweakCategory { Power, Cpu, Gpu, Memory, Latency, Network, Background, Storage, Display }

/// <summary>
/// A single reversible change to the system. Every tweak must be able to restore the
/// exact previous state from the snapshot it returned when applied.
/// </summary>
public interface ITweak
{
    string Id { get; }
    string Name { get; }
    /// <summary>Plain-language explanation of what it does and why it helps.</summary>
    string Description { get; }
    RiskLevel Risk { get; }
    TweakCategory Category { get; }
    /// <summary>True when the tweak only lasts while a game runs (reverted when the game closes).</summary>
    bool GameSessionOnly { get; }

    /// <summary>Whether this tweak can run on this PC at all (hardware, Windows version, permissions).</summary>
    Task<bool> IsSupportedAsync(CancellationToken ct = default);
    Task<bool> IsAppliedAsync(CancellationToken ct = default);
    /// <summary>Applies the change and returns everything needed to undo it.</summary>
    Task<TweakSnapshot> ApplyAsync(CancellationToken ct = default);
    Task RevertAsync(TweakSnapshot snapshot, CancellationToken ct = default);

    /// <summary>
    /// Whether a snapshot read from disk is one this tweak could have produced (only its own keys, values in
    /// the expected format). Snapshots that fail this check are discarded and never reverted, so a tampered
    /// state file cannot make the app write arbitrary values.
    /// </summary>
    bool AcceptsSnapshot(TweakSnapshot snapshot) => snapshot.PreviousValues is not null;
}

/// <summary>The state a tweak replaced. Persisted to disk so it survives crashes and reboots.</summary>
public sealed record TweakSnapshot(
    string TweakId,
    DateTimeOffset AppliedAt,
    IReadOnlyDictionary<string, string?> PreviousValues)
{
    public static TweakSnapshot Create(string tweakId, IReadOnlyDictionary<string, string?> previous) =>
        new(tweakId, DateTimeOffset.UtcNow, previous);
}
