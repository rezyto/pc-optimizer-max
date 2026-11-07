using PcMax.Core.Doctor;
using PcMax.Core.Platform;

namespace PcMax.App.Services.Fakes;

/// <summary>A gaming PC with typical mistakes: 60 Hz on a 144 Hz monitor, XMP off, Wi-Fi, Game DVR on.</summary>
public sealed class FakeHardwareInspector : IHardwareInspector
{
    private readonly FakeState _state;
    public FakeHardwareInspector(FakeState state) => _state = state;

    public async Task<HardwareProfile> InspectAsync(CancellationToken ct = default)
    {
        await Task.Delay(1200, ct);
        var hz = _state.IsApplied("display.max-refresh") ? 144 : 60;
        return new HardwareProfile
        {
            CpuName = "AMD Ryzen 5 5600X",
            CpuVendor = "AMD",
            CpuCores = 6,
            IsLaptop = false,
            MemoryModules = new[]
            {
                new MemoryModule("DIMM_A2", 8, 2133, 3200),
                new MemoryModule("DIMM_B2", 8, 2133, 3200),
            },
            TotalRamGb = 16,
            Gpus = new[] { new GpuInfo("NVIDIA GeForce RTX 3060", true, 12, "560.94") },
            Monitors = new[] { new MonitorInfo("AOC 24G2", hz, 144, 1920, 1080, true) },
            ActivePowerPlanName = "Equilibrado",
            ActivePowerPlanIsHighPerformance = false,
            WindowsGameModeEnabled = _state.IsApplied("windows.game-mode"),
            HardwareAcceleratedGpuScheduling = true,
            GameDvrBackgroundRecording = !_state.IsApplied("windows.disable-background-recording"),
            SystemDriveFreeGb = 84,
            SystemDriveIsHdd = false,
            StartupAppsEnabled = 9,
            DaysSinceGpuDriver = 214,
            MaxRecentCpuTempC = 71,
            MaxRecentGpuTempC = 76,
            NetworkIsWifi = true,
        };
    }
}
