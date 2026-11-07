using PcMax.Core.Sensors;

namespace PcMax.App.Services.Fakes;

/// <summary>Slowly varying, realistic readings (random walk around a target that drifts).</summary>
public sealed class FakeSensorProvider : ISensorProvider
{
    private readonly FakeState _state;
    private readonly Random _rng = new(7);
    private readonly object _gate = new();
    private double _cpuLoad = 18, _gpuLoad = 12, _cpuTemp = 52, _gpuTemp = 47, _ram = 9.6;
    private int _tick;

    public FakeSensorProvider(FakeState state) => _state = state;

    public bool CpuSensorsNeedDriver => false;

    public SensorSnapshot Read()
    {
        lock (_gate)
        {
            _tick++;
            // A "busy" phase every ~40 s makes the graphs interesting.
            var busy = Math.Sin(_tick / 12.0) > 0.2;
            var cpuTarget = busy ? 62 : 16;
            var gpuTarget = busy ? 88 : 9;
            _cpuLoad = Step(_cpuLoad, cpuTarget, 7, 0, 100);
            _gpuLoad = Step(_gpuLoad, gpuTarget, 8, 0, 100);
            _cpuTemp = Step(_cpuTemp, 44 + _cpuLoad * 0.38, 1.2, 30, 99);
            _gpuTemp = Step(_gpuTemp, 40 + _gpuLoad * 0.36, 1.0, 30, 95);
            _ram = Step(_ram, busy ? 13.4 : 9.8, 0.25, 4, 15.6);
            return new SensorSnapshot
            {
                Timestamp = DateTimeOffset.UtcNow,
                CpuLoadPercent = _cpuLoad,
                CpuTemperatureC = _cpuTemp,
                CpuClockMhz = 3600 + _cpuLoad * 12,
                CpuPowerW = 25 + _cpuLoad * 0.9,
                CpuThrottling = false,
                GpuLoadPercent = _gpuLoad,
                GpuTemperatureC = _gpuTemp,
                GpuClockMhz = 600 + _gpuLoad * 19,
                GpuMemoryUsedMb = 900 + _gpuLoad * 60,
                GpuPowerW = 20 + _gpuLoad * 1.9,
                RamUsedGb = _ram,
                RamTotalGb = 16,
                StorageTemperatureC = 38,
                FanRpm = 900 + _cpuLoad * 9,
            };
        }
    }

    private double Step(double v, double target, double noise, double min, double max)
    {
        v += (target - v) * 0.18 + (_rng.NextDouble() - 0.5) * noise;
        return Math.Clamp(v, min, max);
    }

    public void Dispose() { }
}
