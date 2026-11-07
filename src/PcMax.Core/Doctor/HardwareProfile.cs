namespace PcMax.Core.Doctor;

/// <summary>Everything the PC Doctor needs to know about the machine. Collected by the platform layer.</summary>
public sealed record HardwareProfile
{
    public string? CpuName { get; init; }
    public string? CpuVendor { get; init; } // "AMD" | "Intel"
    public int? CpuCores { get; init; }
    public bool IsLaptop { get; init; }
    public bool? OnBattery { get; init; }

    public IReadOnlyList<MemoryModule> MemoryModules { get; init; } = Array.Empty<MemoryModule>();
    public double? TotalRamGb { get; init; }

    public IReadOnlyList<GpuInfo> Gpus { get; init; } = Array.Empty<GpuInfo>();
    public IReadOnlyList<MonitorInfo> Monitors { get; init; } = Array.Empty<MonitorInfo>();

    public string? ActivePowerPlanName { get; init; }
    public bool? ActivePowerPlanIsHighPerformance { get; init; }
    public bool? WindowsGameModeEnabled { get; init; }
    public bool? HardwareAcceleratedGpuScheduling { get; init; }
    public bool? GameDvrBackgroundRecording { get; init; }
    public double? SystemDriveFreeGb { get; init; }
    public bool? SystemDriveIsHdd { get; init; }
    public int? StartupAppsEnabled { get; init; }
    public int? DaysSinceGpuDriver { get; init; }
    public double? MaxRecentCpuTempC { get; init; }
    public double? MaxRecentGpuTempC { get; init; }
    public bool? NetworkIsWifi { get; init; }
}

public sealed record MemoryModule(string Slot, double CapacityGb, int? ConfiguredSpeedMhz, int? RatedSpeedMhz);

public sealed record GpuInfo(string Name, bool IsDiscrete, double? VramGb, string? DriverVersion);

public sealed record MonitorInfo(string Name, int CurrentRefreshHz, int MaxRefreshHz, int Width, int Height, bool IsPrimary);
