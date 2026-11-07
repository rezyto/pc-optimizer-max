namespace PcMax.Core.Sensors;

/// <summary>One reading of the PC's vital signs. Any value can be null when the hardware does not report it.</summary>
public sealed record SensorSnapshot
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public double? CpuLoadPercent { get; init; }
    public double? CpuTemperatureC { get; init; }
    public double? CpuClockMhz { get; init; }
    public double? CpuPowerW { get; init; }
    public bool? CpuThrottling { get; init; }
    public double? GpuLoadPercent { get; init; }
    public double? GpuTemperatureC { get; init; }
    public double? GpuClockMhz { get; init; }
    public double? GpuMemoryUsedMb { get; init; }
    public double? GpuPowerW { get; init; }
    public double? RamUsedGb { get; init; }
    public double? RamTotalGb { get; init; }
    public double? RamUsedPercent => RamUsedGb is { } u && RamTotalGb is { } t && t > 0 ? u / t * 100 : null;
    public double? StorageTemperatureC { get; init; }
    public double? FanRpm { get; init; }
}

public interface ISensorProvider : IDisposable
{
    /// <summary>True when CPU temperatures need the PawnIO driver and it is not installed.</summary>
    bool CpuSensorsNeedDriver { get; }
    SensorSnapshot Read();
}
