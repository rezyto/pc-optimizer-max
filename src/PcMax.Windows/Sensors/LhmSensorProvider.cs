using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LibreHardwareMonitor.Hardware;
using PcMax.Core.Sensors;
using PcMax.Windows.Native;

namespace PcMax.Windows.Sensors;

/// <summary>
/// Sensors through LibreHardwareMonitorLib. CPU temperature/power (and motherboard Super I/O fans)
/// need the signed PawnIO driver; when it is missing <see cref="CpuSensorsNeedDriver"/> is true and
/// those values stay null, while GPU, storage, RAM and CPU load keep working.
/// LHM is opened on a background thread because Open() can take seconds; until then only RAM and
/// CPU load (GetSystemTimes) are reported. <see cref="Read"/> is thread-safe and never throws.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class LhmSensorProvider : ISensorProvider
{
    private static readonly TimeSpan SlowInterval = TimeSpan.FromSeconds(15);

    private readonly object _gate = new();
    private Computer? _computer;
    private IHardware? _cpu;
    private IHardware? _gpu;
    private readonly List<IHardware> _storage = new();
    private readonly List<IHardware> _boards = new();
    private bool _disposed;
    private DateTime _lastSlowUpdate = DateTime.MinValue;
    private double? _storageTemp;
    private double? _boardFan;

    // GetSystemTimes fallback for CPU load
    private long _lastIdle, _lastKernel, _lastUser;

    public LhmSensorProvider()
    {
        CpuSensorsNeedDriver = !PawnIoInstalled();
        _ = Task.Run(Open);
    }

    public bool CpuSensorsNeedDriver { get; }

    private static bool PawnIoInstalled()
    {
        try { return LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled; }
        catch (Exception ex)
        {
            Log.Warn("Não foi possível verificar o driver PawnIO", ex);
            return false;
        }
    }

    private void Open()
    {
        Computer? c = null;
        try
        {
            c = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsStorageEnabled = true,
                // Super I/O fan chips are read through PawnIO; skip them when it is not installed.
                IsMotherboardEnabled = !CpuSensorsNeedDriver,
                IsMemoryEnabled = false,      // RAM comes from GlobalMemoryStatusEx
                IsNetworkEnabled = false,
                IsControllerEnabled = false,
                IsPsuEnabled = false,
                IsBatteryEnabled = false,
            };
            c.Open();
        }
        catch (Exception ex)
        {
            Log.Warn("LibreHardwareMonitor: falhou ao abrir com CPU/placa-mãe; a tentar só GPU e discos", ex);
            try { c?.Close(); } catch { }
            try
            {
                c = new Computer { IsGpuEnabled = true, IsStorageEnabled = true };
                c.Open();
            }
            catch (Exception ex2)
            {
                Log.Error("LibreHardwareMonitor indisponível", ex2);
                try { c?.Close(); } catch { }
                return;
            }
        }

        lock (_gate)
        {
            if (_disposed)
            {
                try { c.Close(); } catch { }
                return;
            }
            _computer = c;
            foreach (var h in c.Hardware)
            {
                switch (h.HardwareType)
                {
                    case HardwareType.Cpu: _cpu ??= h; break;
                    case HardwareType.Storage: _storage.Add(h); break;
                    case HardwareType.Motherboard: _boards.Add(h); break;
                }
            }
            _gpu = PickGpu(c.Hardware);
            Log.Info($"Sensores: CPU={_cpu?.Name ?? "-"}, GPU={_gpu?.Name ?? "-"}, discos={_storage.Count}, PawnIO={(CpuSensorsNeedDriver ? "não" : "sim")}");
        }
    }

    /// <summary>Discrete first (NVIDIA, then AMD with the most VRAM), Intel last.</summary>
    private static IHardware? PickGpu(IEnumerable<IHardware> all)
    {
        var gpus = all.Where(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel).ToList();
        foreach (var g in gpus) { try { g.Update(); } catch { } }
        return gpus
            .OrderBy(g => g.HardwareType switch { HardwareType.GpuNvidia => 0, HardwareType.GpuAmd => 1, _ => 2 })
            .ThenByDescending(g => Find(g, SensorType.SmallData, "GPU Memory Total")?.Value ?? 0)
            .FirstOrDefault();
    }

    public SensorSnapshot Read()
    {
        lock (_gate)
        {
            try
            {
                return ReadCore();
            }
            catch (Exception ex)
            {
                Log.Warn("Leitura de sensores falhou", ex);
                return new SensorSnapshot();
            }
        }
    }

    private SensorSnapshot ReadCore()
    {
        var (ramUsed, ramTotal) = Ram();
        var snap = new SensorSnapshot { RamUsedGb = ramUsed, RamTotalGb = ramTotal };
        if (_disposed) return snap;

        double? cpuLoad = null, cpuTemp = null, cpuClock = null, cpuPower = null;
        bool? throttling = null;
        if (_cpu is not null && SafeUpdate(_cpu))
        {
            cpuLoad = Value(Find(_cpu, SensorType.Load, "CPU Total"));
            cpuTemp = CpuTemperature(_cpu);
            cpuClock = CpuClock(_cpu);
            cpuPower = Value(Find(_cpu, SensorType.Power, "CPU Package") ?? Find(_cpu, SensorType.Power, "Package")
                ?? FindContains(_cpu, SensorType.Power, "Package"));
            throttling = Throttling(_cpu);
        }
        cpuLoad ??= SystemTimesLoad();

        double? gpuLoad = null, gpuTemp = null, gpuClock = null, gpuMem = null, gpuPower = null, gpuFan = null;
        if (_gpu is not null && SafeUpdate(_gpu))
        {
            gpuLoad = Value(Find(_gpu, SensorType.Load, "GPU Core") ?? Find(_gpu, SensorType.Load, "D3D 3D"));
            gpuTemp = Value(Find(_gpu, SensorType.Temperature, "GPU Core") ?? FirstOf(_gpu, SensorType.Temperature), max: 125);
            gpuClock = Value(Find(_gpu, SensorType.Clock, "GPU Core"));
            gpuMem = Value(Find(_gpu, SensorType.SmallData, "GPU Memory Used")
                ?? Find(_gpu, SensorType.SmallData, "D3D Dedicated Memory Used"), max: 1_000_000);
            gpuPower = Value(Find(_gpu, SensorType.Power, "GPU Package") ?? Find(_gpu, SensorType.Power, "GPU Power")
                ?? Find(_gpu, SensorType.Power, "GPU Core") ?? FirstOf(_gpu, SensorType.Power), max: 2000);
            gpuFan = Value(FirstOf(_gpu, SensorType.Fan), max: 20000);
        }

        // Storage SMART and Super I/O reads are slow: refresh them every 15 s only.
        if (DateTime.UtcNow - _lastSlowUpdate >= SlowInterval)
        {
            _lastSlowUpdate = DateTime.UtcNow;
            _storageTemp = StorageTemperature();
            _boardFan = BoardFan();
        }

        return snap with
        {
            CpuLoadPercent = Clamp(cpuLoad, 0, 100),
            CpuTemperatureC = cpuTemp,
            CpuClockMhz = cpuClock,
            CpuPowerW = cpuPower,
            CpuThrottling = throttling,
            GpuLoadPercent = Clamp(gpuLoad, 0, 100),
            GpuTemperatureC = gpuTemp,
            GpuClockMhz = gpuClock,
            GpuMemoryUsedMb = gpuMem,
            GpuPowerW = gpuPower,
            StorageTemperatureC = _storageTemp,
            FanRpm = _boardFan ?? gpuFan,
        };
    }

    // ------------------------------------------------------------------ CPU helpers

    private static readonly string[] CpuTempNames =
    {
        "CPU Package", "Core (Tctl/Tdie)", "Core (Tctl)", "Tctl/Tdie", "Core (Tdie)", "Package", "Core Max", "Core Average",
    };

    private static double? CpuTemperature(IHardware cpu)
    {
        foreach (var n in CpuTempNames)
        {
            if (Value(Find(cpu, SensorType.Temperature, n), min: 1, max: 125) is { } v) return v;
        }
        // Fallback: hottest core/CCD sensor, ignoring "Distance to TjMax" readings.
        var vals = cpu.Sensors
            .Where(s => s.SensorType == SensorType.Temperature && !s.Name.Contains("Distance", StringComparison.OrdinalIgnoreCase))
            .Select(s => Value(s, min: 1, max: 125))
            .Where(v => v is not null)
            .ToList();
        return vals.Count > 0 ? vals.Max() : null;
    }

    private static double? CpuClock(IHardware cpu)
    {
        var cores = cpu.Sensors.Where(s => s.SensorType == SensorType.Clock && s.Name.StartsWith("Core #", StringComparison.Ordinal)).ToList();
        var plain = cores.Where(s => !s.Name.Contains("Effective", StringComparison.OrdinalIgnoreCase)).ToList();
        var use = plain.Count > 0 ? plain : cores;
        var vals = use.Select(s => Value(s, min: 1, max: 10000)).Where(v => v is not null).Select(v => v!.Value).ToList();
        return vals.Count > 0 ? Math.Round(vals.Average()) : null;
    }

    /// <summary>
    /// Only when the CPU exposes throttling/limit indicators (e.g. Intel "Core Thermal Throttling",
    /// "Package ... Limit"): true if any is active. Null when no such sensor exists.
    /// </summary>
    private static bool? Throttling(IHardware cpu)
    {
        var flags = cpu.Sensors.Where(s =>
            s.SensorType is not (SensorType.Power or SensorType.Temperature or SensorType.Clock or SensorType.Voltage) &&
            (s.Name.Contains("Throttl", StringComparison.OrdinalIgnoreCase)
             || (s.Name.Contains("Limit", StringComparison.OrdinalIgnoreCase) && s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase))))
            .ToList();
        if (flags.Count == 0) return null;
        var any = false;
        var known = false;
        foreach (var s in flags)
        {
            if (s.Value is not { } v || float.IsNaN(v)) continue;
            known = true;
            if (v > 0) any = true;
        }
        return known ? any : null;
    }

    private double? SystemTimesLoad()
    {
        if (!NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user)) return null;
        var dIdle = idle - _lastIdle;
        var dTotal = (kernel - _lastKernel) + (user - _lastUser); // kernel time includes idle time
        var first = _lastKernel == 0;
        _lastIdle = idle; _lastKernel = kernel; _lastUser = user;
        if (first || dTotal <= 0) return null;
        return Math.Round((1.0 - (double)dIdle / dTotal) * 100.0, 1);
    }

    // ------------------------------------------------------------------ slow sensors

    private double? StorageTemperature()
    {
        double? max = null;
        foreach (var d in _storage)
        {
            if (!SafeUpdate(d)) continue;
            var v = Value(Find(d, SensorType.Temperature, "Temperature") ?? FirstOf(d, SensorType.Temperature), min: 1, max: 110);
            if (v is { } t && (max is null || t > max)) max = t;
        }
        return max;
    }

    /// <summary>Motherboard fan: the CPU fan when named, otherwise the fastest spinning one.</summary>
    private double? BoardFan()
    {
        var fans = new List<ISensor>();
        foreach (var b in _boards)
        {
            if (!SafeUpdate(b)) continue;
            foreach (var sub in b.SubHardware)
            {
                if (!SafeUpdate(sub)) continue;
                fans.AddRange(sub.Sensors.Where(s => s.SensorType == SensorType.Fan));
            }
            fans.AddRange(b.Sensors.Where(s => s.SensorType == SensorType.Fan));
        }
        var spinning = fans.Where(f => Value(f, min: 1, max: 20000) is not null).ToList();
        if (spinning.Count == 0) return null;
        var cpuFan = spinning.FirstOrDefault(f => f.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase));
        return Value(cpuFan) ?? spinning.Max(f => Value(f));
    }

    // ------------------------------------------------------------------ RAM

    private static (double? Used, double? Total) Ram()
    {
        var m = new NativeMethods.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>() };
        if (!NativeMethods.GlobalMemoryStatusEx(ref m) || m.ullTotalPhys == 0) return (null, null);
        const double Gb = 1024.0 * 1024 * 1024;
        return (Math.Round((m.ullTotalPhys - m.ullAvailPhys) / Gb, 2), Math.Round(m.ullTotalPhys / Gb, 2));
    }

    // ------------------------------------------------------------------ generic helpers

    private static bool SafeUpdate(IHardware h)
    {
        try { h.Update(); return true; }
        catch (Exception ex)
        {
            Log.Warn($"Sensor {h.Name}: Update falhou", ex);
            return false;
        }
    }

    private static ISensor? Find(IHardware h, SensorType type, string name) =>
        h.Sensors.FirstOrDefault(s => s.SensorType == type && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    private static ISensor? FindContains(IHardware h, SensorType type, string part) =>
        h.Sensors.FirstOrDefault(s => s.SensorType == type && s.Name.Contains(part, StringComparison.OrdinalIgnoreCase));

    private static ISensor? FirstOf(IHardware h, SensorType type) => h.Sensors.FirstOrDefault(s => s.SensorType == type && s.Value is not null);

    private static double? Value(ISensor? s, double min = 0, double max = 100_000)
    {
        if (s?.Value is not { } v || float.IsNaN(v) || float.IsInfinity(v)) return null;
        return v < min || v > max ? null : Math.Round(v, 1);
    }

    private static double? Clamp(double? v, double min, double max) => v is { } x ? Math.Clamp(x, min, max) : null;

    public void Dispose()
    {
        Computer? c;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            c = _computer;
            _computer = null;
            _cpu = null;
            _gpu = null;
            _storage.Clear();
            _boards.Clear();
        }
        try { c?.Close(); } catch (Exception ex) { Log.Warn("LibreHardwareMonitor: Close falhou", ex); }
    }
}
