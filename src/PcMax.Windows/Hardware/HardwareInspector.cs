using System.Globalization;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using PcMax.Core.Doctor;
using PcMax.Core.Platform;
using PcMax.Core.Settings;
using PcMax.Windows.Native;
using PcMax.Windows.Tweaks;

namespace PcMax.Windows.Hardware;

/// <summary>
/// Collects the <see cref="HardwareProfile"/> with WMI, the registry (read-only) and Win32 calls.
/// Every probe is isolated: if one fails, only its field stays null/empty.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed partial class HardwareInspector : IHardwareInspector
{
    private readonly AppPaths _paths;
    private readonly IStartupManager _startup;

    public HardwareInspector(AppPaths paths, IStartupManager startup)
    {
        _paths = paths;
        _startup = startup;
    }

    public Task<HardwareProfile> InspectAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        var cpu = Try("cpu", ReadCpu);
        ct.ThrowIfCancellationRequested();
        var modules = Try("memória", ReadMemoryModules) ?? Array.Empty<MemoryModule>();
        var gpus = Try("gpu", ReadGpus) ?? new List<(GpuInfo Info, DateTime? DriverDate)>();
        ct.ThrowIfCancellationRequested();
        var monitors = Try("monitores", ReadMonitors) ?? Array.Empty<MonitorInfo>();
        var power = Try("plano de energia", ReadPowerPlan);
        ct.ThrowIfCancellationRequested();
        var (driveFree, driveHdd) = Try("disco", () => (SystemDriveFreeGb(), SystemDriveIsHdd()));

        double? totalRam = modules.Count > 0 ? Math.Round(modules.Sum(m => m.CapacityGb), 1) : Try("ram", TotalPhysicalGb);
        var mainGpu = gpus.FirstOrDefault(g => g.Info.IsDiscrete);
        if (mainGpu.Info is null) mainGpu = gpus.FirstOrDefault();

        return new HardwareProfile
        {
            CpuName = cpu?.Name,
            CpuVendor = cpu?.Vendor,
            CpuCores = cpu?.Cores,
            IsLaptop = IsLaptopCached(),
            OnBattery = Try("bateria", OnBattery),
            MemoryModules = modules,
            TotalRamGb = totalRam,
            Gpus = gpus.Select(g => g.Info).ToList(),
            Monitors = monitors,
            ActivePowerPlanName = power?.Name,
            ActivePowerPlanIsHighPerformance = power?.IsHigh,
            WindowsGameModeEnabled = Try("modo de jogo", GameModeEnabled),
            HardwareAcceleratedGpuScheduling = Try("hags", Hags),
            GameDvrBackgroundRecording = Try("game dvr", GameDvrBackgroundRecording),
            SystemDriveFreeGb = driveFree,
            SystemDriveIsHdd = driveHdd,
            StartupAppsEnabled = Try("arranque", () => (int?)_startup.List().Count(i => i.Enabled)),
            DaysSinceGpuDriver = mainGpu.DriverDate is { } dd ? Math.Max(0, (int)(DateTime.Now - dd).TotalDays) : null,
            NetworkIsWifi = Try("rede", NetworkIsWifi),
        };
    }, ct);

    private static T? Try<T>(string what, Func<T> probe)
    {
        try { return probe(); }
        catch (Exception ex)
        {
            Log.Warn($"Inspeção ({what}) falhou", ex);
            return default;
        }
    }

    // ------------------------------------------------------------------ CPU

    private sealed record CpuData(string? Name, string? Vendor, int? Cores);

    private static CpuData ReadCpu()
    {
        string? name = null, vendor = null;
        int cores = 0;
        using var s = new ManagementObjectSearcher("SELECT Name, Manufacturer, NumberOfCores FROM Win32_Processor");
        using var results = s.Get();
        foreach (ManagementObject o in results)
        {
            using (o)
            {
                name ??= (o["Name"] as string)?.Trim();
                var m = o["Manufacturer"] as string ?? "";
                vendor ??= m.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? "Intel"
                    : m.Contains("AMD", StringComparison.OrdinalIgnoreCase) ? "AMD"
                    : string.IsNullOrWhiteSpace(m) ? null : m.Trim();
                if (o["NumberOfCores"] is uint c) cores += (int)c; // sum across sockets
            }
        }
        return new CpuData(name, vendor, cores > 0 ? cores : null);
    }

    // ------------------------------------------------------------------ laptop / battery

    private static readonly Lazy<bool> LaptopLazy = new(DetectLaptop);
    public static bool IsLaptopCached() => LaptopLazy.Value;

    private static readonly HashSet<int> LaptopChassis = new() { 8, 9, 10, 11, 14, 30, 31, 32 };
    private static readonly HashSet<int> DesktopChassis = new() { 3, 4, 5, 6, 7, 13, 15, 16, 17, 23, 24, 35, 36 };

    private static bool DetectLaptop()
    {
        try
        {
            using var s = new ManagementObjectSearcher("SELECT ChassisTypes FROM Win32_SystemEnclosure");
            using var results = s.Get();
            foreach (ManagementObject o in results)
            {
                using (o)
                {
                    if (o["ChassisTypes"] is not ushort[] types) continue;
                    if (types.Any(t => LaptopChassis.Contains(t))) return true;
                    if (types.Any(t => DesktopChassis.Contains(t))) return false;
                }
            }
        }
        catch (Exception ex) { Log.Warn("Win32_SystemEnclosure falhou", ex); }

        // Unknown chassis: a system battery means laptop/tablet (a UPS does not set this flag).
        try
        {
            if (NativeMethods.GetSystemPowerStatus(out var st))
                return st.BatteryFlag != 128 && st.BatteryFlag != 255;
        }
        catch { }
        return false;
    }

    private static bool? OnBattery()
    {
        if (!NativeMethods.GetSystemPowerStatus(out var st)) return null;
        return st.ACLineStatus switch { 0 => true, 1 => false, _ => null };
    }

    // ------------------------------------------------------------------ memory

    /// <summary>
    /// Win32_PhysicalMemory (SMBIOS type 17). Limitation: "Speed" is the module's maximum speed as
    /// stored in SMBIOS, which is usually the JEDEC speed (e.g. 4800 for DDR5) and often NOT the
    /// XMP/EXPO profile speed printed on the box. ConfiguredClockSpeed is the speed actually in use.
    /// </summary>
    private static IReadOnlyList<MemoryModule> ReadMemoryModules()
    {
        var list = new List<MemoryModule>();
        using var s = new ManagementObjectSearcher("SELECT DeviceLocator, BankLabel, Capacity, ConfiguredClockSpeed, Speed FROM Win32_PhysicalMemory");
        using var results = s.Get();
        foreach (ManagementObject o in results)
        {
            using (o)
            {
                var locator = (o["DeviceLocator"] as string)?.Trim();
                var bank = (o["BankLabel"] as string)?.Trim();
                var slot = (string.IsNullOrEmpty(locator), string.IsNullOrEmpty(bank)) switch
                {
                    (false, false) when !string.Equals(locator, bank, StringComparison.OrdinalIgnoreCase) => $"{locator} ({bank})",
                    (false, _) => locator!,
                    (true, false) => bank!,
                    _ => $"Slot {list.Count + 1}",
                };
                var capacity = o["Capacity"] is ulong cap ? cap / 1024.0 / 1024 / 1024 : 0;
                int? configured = o["ConfiguredClockSpeed"] is uint c && c > 0 ? (int)c : null;
                int? rated = o["Speed"] is uint r && r > 0 ? (int)r : null;
                list.Add(new MemoryModule(slot, Math.Round(capacity, 2), configured, rated));
            }
        }
        return list;
    }

    private static double? TotalPhysicalGb()
    {
        var m = new NativeMethods.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>() };
        return NativeMethods.GlobalMemoryStatusEx(ref m) ? Math.Round(m.ullTotalPhys / 1024.0 / 1024 / 1024, 1) : null;
    }

    // ------------------------------------------------------------------ GPU

    private const string DisplayClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    private static List<(GpuInfo Info, DateTime? DriverDate)> ReadGpus()
    {
        var vramByName = VramFromRegistry();
        var list = new List<(GpuInfo, DateTime?)>();
        using var s = new ManagementObjectSearcher("SELECT Name, AdapterRAM, DriverVersion, DriverDate, PNPDeviceID FROM Win32_VideoController");
        using var results = s.Get();
        foreach (ManagementObject o in results)
        {
            using (o)
            {
                var name = (o["Name"] as string)?.Trim();
                if (string.IsNullOrEmpty(name) || IsVirtualAdapter(name, o["PNPDeviceID"] as string)) continue;

                // AdapterRAM is a uint32 (caps at 4 GB): prefer the driver's 64-bit value from the registry.
                double? vram = vramByName.TryGetValue(name, out var q) && q > 0 ? q / 1024.0 / 1024 / 1024 : null;
                if (vram is null && o["AdapterRAM"] is uint ar && ar > 0) vram = ar / 1024.0 / 1024 / 1024;

                DateTime? driverDate = null;
                if (o["DriverDate"] is string ds && !string.IsNullOrWhiteSpace(ds))
                {
                    try { driverDate = ManagementDateTimeConverter.ToDateTime(ds); } catch { }
                }
                list.Add((new GpuInfo(name, IsDiscreteGpu(name), vram is { } v ? Math.Round(v, 1) : null, o["DriverVersion"] as string), driverDate));
            }
        }
        return list;
    }

    private static bool IsVirtualAdapter(string name, string? pnpId) =>
        name.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Remote Display", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Parsec", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Virtual", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Mirage", StringComparison.OrdinalIgnoreCase)
        || (pnpId?.StartsWith("ROOT\\", StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>
    /// Name heuristics. Integrated: Intel UHD/HD/Iris/"Arc Graphics" (Meteor/Lunar Lake iGPU, no model number),
    /// AMD "Radeon(TM) Graphics", "Radeon 780M"-style and "Vega N" APUs. Everything else (GeForce, RX, Arc A/B) is discrete.
    /// </summary>
    internal static bool IsDiscreteGpu(string name)
    {
        var n = name.ToUpperInvariant();
        if (n.Contains("INTEL"))
            return IntelArcDiscrete().IsMatch(name);
        if (n.Contains("AMD") || n.Contains("RADEON") || n.Contains("ATI "))
            return !AmdIntegrated().IsMatch(name);
        return true; // NVIDIA and others
    }

    [GeneratedRegex(@"Arc(\(TM\))?\s+(Pro\s+)?[AB]\d{2,3}", RegexOptions.IgnoreCase)]
    private static partial Regex IntelArcDiscrete();

    [GeneratedRegex(@"Radeon(\(TM\))?\s+(Graphics|Vega|\d{3}M\b|R[2-7]\s+Graphics)|Vega\s+\d+\s+Graphics", RegexOptions.IgnoreCase)]
    private static partial Regex AmdIntegrated();

    private static Dictionary<string, long> VramFromRegistry()
    {
        var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var cls = root.OpenSubKey(DisplayClassKey);
            if (cls is null) return map;
            foreach (var sub in cls.GetSubKeyNames())
            {
                if (sub.Length != 4 || !sub.All(char.IsDigit)) continue;
                using var k = cls.OpenSubKey(sub);
                if (k?.GetValue("DriverDesc") is not string desc) continue;
                var bytes = k.GetValue("HardwareInformation.qwMemorySize") switch
                {
                    long l => l,
                    byte[] b when b.Length >= 8 => BitConverter.ToInt64(b, 0),
                    _ => 0L,
                };
                if (bytes > 0) map[desc.Trim()] = bytes;
            }
        }
        catch (Exception ex) { Log.Warn("VRAM do registo indisponível", ex); }
        return map;
    }

    // ------------------------------------------------------------------ monitors

    private static IReadOnlyList<MonitorInfo> ReadMonitors() =>
        Displays.List().Select(d => new MonitorInfo(d.FriendlyName, d.CurrentHz, d.MaxHz, d.Width, d.Height, d.IsPrimary)).ToList();

    // ------------------------------------------------------------------ power plan

    private (string? Name, bool IsHigh)? ReadPowerPlan()
    {
        if (PowerPlans.GetActive() is not { } g) return null;
        return (PowerPlans.GetFriendlyName(g), PowerPlans.IsHighPerformance(g, _paths));
    }

    // ------------------------------------------------------------------ Windows gaming settings

    private static bool? GameModeEnabled()
    {
        var v = RegistryValue.ReadDword(RegistryHive.CurrentUser, GameModeTweak.GameBarKey, "AutoGameModeEnabled");
        if (v is { } x) return x != 0;
        // Missing value: Game Mode is on by default since Windows 10 1903 (build 18362).
        return Environment.OSVersion.Version.Build >= 18362;
    }

    private static bool? Hags()
    {
        var v = RegistryValue.ReadDword(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode");
        return v is { } x ? x == 2 : null;
    }

    /// <summary>
    /// "Record what happened" (background recording) is on only when captures are allowed
    /// (GameDVR_Enabled and AppCaptureEnabled not 0) and HistoricalCaptureEnabled is 1.
    /// </summary>
    private static bool? GameDvrBackgroundRecording()
    {
        var dvr = RegistryValue.ReadDword(RegistryHive.CurrentUser, BackgroundRecordingTweak.GameConfigStoreKey, "GameDVR_Enabled");
        var app = RegistryValue.ReadDword(RegistryHive.CurrentUser, BackgroundRecordingTweak.GameDvrKey, "AppCaptureEnabled");
        var hist = RegistryValue.ReadDword(RegistryHive.CurrentUser, BackgroundRecordingTweak.GameDvrKey, "HistoricalCaptureEnabled");
        if (dvr == 0 || app == 0) return false;
        return hist == 1;
    }

    // ------------------------------------------------------------------ storage

    private static string SystemDriveLetter() =>
        (Environment.GetEnvironmentVariable("SystemDrive") ?? Path.GetPathRoot(Environment.SystemDirectory) ?? "C:")
            .TrimEnd('\\', ':');

    private static double? SystemDriveFreeGb()
    {
        try
        {
            var d = new DriveInfo(SystemDriveLetter() + ":\\");
            return Math.Round(d.AvailableFreeSpace / 1024.0 / 1024 / 1024, 1);
        }
        catch (Exception ex)
        {
            Log.Warn("Espaço livre do disco do sistema indisponível", ex);
            return null;
        }
    }

    /// <summary>MSFT_Partition (drive letter) -> disk number -> MSFT_PhysicalDisk.MediaType (3 = HDD, 4 = SSD, 5 = SCM).</summary>
    private static bool? SystemDriveIsHdd()
    {
        try
        {
            var letter = char.ToUpperInvariant(SystemDriveLetter()[0]);
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
            uint? diskNumber = null;
            using (var s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT DriveLetter, DiskNumber FROM MSFT_Partition")))
            using (var results = s.Get())
            {
                foreach (ManagementObject o in results)
                {
                    using (o)
                    {
                        if (o["DriveLetter"] is char c && char.ToUpperInvariant(c) == letter && o["DiskNumber"] is uint n)
                            diskNumber = n;
                    }
                }
            }
            if (diskNumber is null) return null;

            using var ps = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT DeviceId, MediaType FROM MSFT_PhysicalDisk"));
            using var pres = ps.Get();
            foreach (ManagementObject o in pres)
            {
                using (o)
                {
                    if (!string.Equals(o["DeviceId"] as string, diskNumber.Value.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)) continue;
                    return Convert.ToInt32(o["MediaType"], CultureInfo.InvariantCulture) switch
                    {
                        3 => true,
                        4 or 5 => false,
                        _ => null,
                    };
                }
            }
        }
        catch (Exception ex) { Log.Warn("Tipo de disco indisponível", ex); }
        return null; // storage spaces / RAID / unknown
    }

    // ------------------------------------------------------------------ network

    /// <summary>
    /// The interface Windows would use to reach the internet (route lookup, no traffic sent),
    /// falling back to "any up adapter with a default gateway".
    /// </summary>
    private static bool? NetworkIsWifi()
    {
        var nics = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .ToList();

        try
        {
            // 1.1.1.1 in network byte order. GetBestInterface only consults the routing table.
            var dest = BitConverter.ToUInt32(IPAddress.Parse("1.1.1.1").GetAddressBytes(), 0);
            if (NativeMethods.GetBestInterface(dest, out var idx) == 0)
            {
                foreach (var n in nics)
                {
                    int? nIdx = null;
                    try { nIdx = n.GetIPProperties().GetIPv4Properties()?.Index; } catch { }
                    if (nIdx == (int)idx) return Classify(n);
                }
            }
        }
        catch (Exception ex) { Log.Warn("GetBestInterface falhou", ex); }

        var withGateway = nics.Where(n =>
        {
            try
            {
                return n.GetIPProperties().GatewayAddresses.Any(g =>
                    g.Address is { } a && !a.Equals(IPAddress.Any) && !a.Equals(IPAddress.IPv6Any)
                    && (a.AddressFamily == AddressFamily.InterNetwork || a.AddressFamily == AddressFamily.InterNetworkV6));
            }
            catch { return false; }
        }).ToList();
        if (withGateway.Count == 0) return null;
        if (withGateway.Any(n => Classify(n) == false)) return false;
        return withGateway.Any(n => Classify(n) == true) ? true : null;
    }

    private static bool? Classify(NetworkInterface n)
    {
        if (n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) return true;
        var d = n.Description;
        if (d.Contains("Virtual", StringComparison.OrdinalIgnoreCase) || d.Contains("VPN", StringComparison.OrdinalIgnoreCase)
            || d.Contains("TAP-", StringComparison.OrdinalIgnoreCase) || d.Contains("WireGuard", StringComparison.OrdinalIgnoreCase)
            || d.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase))
            return null; // a VPN/virtual switch hides the real link
        if (d.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase) || d.Contains("Wireless", StringComparison.OrdinalIgnoreCase)
            || d.Contains("WLAN", StringComparison.OrdinalIgnoreCase) || d.Contains("802.11", StringComparison.OrdinalIgnoreCase))
            return true;
        return n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet
            or NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx ? false : null;
    }
}
