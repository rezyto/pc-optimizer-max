namespace PcMax.Core.Tweaks;

public enum HistoryAction { Applied, Reverted, Failed, SkippedRisk, SkippedUnsupported }

public sealed record HistoryEntry(
    DateTimeOffset At,
    string TweakId,
    string TweakName,
    HistoryAction Action,
    string? Detail = null);
