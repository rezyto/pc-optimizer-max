namespace PcMax.Core.Alerts;

public enum AlertSeverity { Info, Warning, Critical }

public sealed record Alert(
    string Key,
    AlertSeverity Severity,
    string Title,
    string Message,
    DateTimeOffset At);
