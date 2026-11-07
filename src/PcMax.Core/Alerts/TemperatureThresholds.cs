namespace PcMax.Core.Alerts;

/// <summary>Temperature limits in °C. Warning = "keep an eye on it", Critical = "act now".</summary>
public sealed record TemperatureThresholds
{
    public double CpuWarning { get; init; } = 85;
    public double CpuCritical { get; init; } = 95;
    public double GpuWarning { get; init; } = 83;
    public double GpuCritical { get; init; } = 90;
    public double StorageWarning { get; init; } = 60;
    public double StorageCritical { get; init; } = 70;
    /// <summary>How long a value must stay above a limit before alerting (ignores short spikes).</summary>
    public int SustainSeconds { get; init; } = 10;

    public TemperatureThresholds Normalize()
    {
        static (double w, double c) Fix(double w, double c, double defW, double defC)
        {
            if (double.IsNaN(w) || w < 30 || w > 110) w = defW;
            if (double.IsNaN(c) || c < 30 || c > 115) c = defC;
            if (c <= w) c = Math.Min(w + 5, 115);
            return (w, c);
        }
        var cpu = Fix(CpuWarning, CpuCritical, 85, 95);
        var gpu = Fix(GpuWarning, GpuCritical, 83, 90);
        var sto = Fix(StorageWarning, StorageCritical, 60, 70);
        return this with
        {
            CpuWarning = cpu.w, CpuCritical = cpu.c,
            GpuWarning = gpu.w, GpuCritical = gpu.c,
            StorageWarning = sto.w, StorageCritical = sto.c,
            SustainSeconds = Math.Clamp(SustainSeconds, 1, 300),
        };
    }
}
