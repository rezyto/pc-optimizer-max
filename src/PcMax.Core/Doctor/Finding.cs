namespace PcMax.Core.Doctor;

public enum FindingSeverity { Good, Tip, Problem, Critical }

/// <summary>How a finding can be fixed: automatically by a tweak, or by following steps.</summary>
public sealed record FixAction(string? TweakId, IReadOnlyList<string> GuideSteps)
{
    public bool IsAutomatic => TweakId is not null;
    public static FixAction Auto(string tweakId) => new(tweakId, Array.Empty<string>());
    public static FixAction Guide(params string[] steps) => new(null, steps);
}

public sealed record Finding(
    string Id,
    FindingSeverity Severity,
    string Title,
    string Explanation,
    /// <summary>Rough expected gain, e.g. "+10–25% nos 1% lows". Shown as an estimate, never a promise.</summary>
    string? EstimatedImpact,
    FixAction? Fix,
    /// <summary>0–100, used to order findings: biggest impact first.</summary>
    int ImpactScore);
