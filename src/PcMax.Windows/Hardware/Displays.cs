using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PcMax.Windows.Native;
using static PcMax.Windows.Native.NativeMethods;

namespace PcMax.Windows.Hardware;

/// <summary>An attached display as seen by GDI (\\.\DISPLAYn).</summary>
internal sealed record DisplayState(
    string DeviceName,
    string FriendlyName,
    bool IsPrimary,
    int Width,
    int Height,
    int BitsPerPel,
    int CurrentHz,
    int MaxHz,
    IReadOnlySet<int> AvailableHz);

/// <summary>Display enumeration and refresh-rate changes (user32 EnumDisplay* / ChangeDisplaySettingsEx).</summary>
[SupportedOSPlatform("windows")]
internal static class Displays
{
    public static IReadOnlyList<DisplayState> List()
    {
        var names = FriendlyNames();
        var result = new List<DisplayState>();
        for (uint i = 0; i < 64; i++)
        {
            var dd = DISPLAY_DEVICE.Create();
            if (!EnumDisplayDevices(null, i, ref dd, 0)) break;
            if ((dd.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0) continue;
            if ((dd.StateFlags & DISPLAY_DEVICE_MIRRORING_DRIVER) != 0) continue;

            var cur = DEVMODE.Create();
            if (!EnumDisplaySettings(dd.DeviceName, ENUM_CURRENT_SETTINGS, ref cur)) continue;
            int w = (int)cur.dmPelsWidth, h = (int)cur.dmPelsHeight, bpp = (int)cur.dmBitsPerPel, hz = (int)cur.dmDisplayFrequency;

            var available = AvailableRefreshRates(dd.DeviceName, w, h, bpp);
            if (hz > 1) available.Add(hz);
            var max = available.Count > 0 ? available.Max() : hz;

            var friendly = names.GetValueOrDefault(dd.DeviceName);
            if (string.IsNullOrWhiteSpace(friendly)) friendly = MonitorDeviceString(dd.DeviceName);
            if (string.IsNullOrWhiteSpace(friendly)) friendly = dd.DeviceName.Replace(@"\\.\", "");

            result.Add(new DisplayState(dd.DeviceName, friendly!, (dd.StateFlags & DISPLAY_DEVICE_PRIMARY_DEVICE) != 0,
                w, h, bpp, hz, max, available));
        }
        return result;
    }

    /// <summary>
    /// Refresh rates available at this resolution. Modes are matched in both orientations (a rotated
    /// screen reports swapped width/height), interlaced modes are ignored, and 0/1 Hz ("hardware default") too.
    /// </summary>
    private static HashSet<int> AvailableRefreshRates(string device, int w, int h, int bpp)
    {
        var set = new HashSet<int>();
        for (var mode = 0; mode < 10000; mode++)
        {
            var dm = DEVMODE.Create();
            if (!EnumDisplaySettings(device, mode, ref dm)) break;
            var sameRes = (dm.dmPelsWidth == w && dm.dmPelsHeight == h) || (dm.dmPelsWidth == h && dm.dmPelsHeight == w);
            if (!sameRes || dm.dmBitsPerPel != bpp) continue;
            if ((dm.dmDisplayFlags & DM_INTERLACED) != 0) continue;
            if (dm.dmDisplayFrequency > 1) set.Add((int)dm.dmDisplayFrequency);
        }
        return set;
    }

    /// <summary>
    /// Changes only the refresh rate of a display, keeping resolution/colour depth/orientation.
    /// Tests with CDS_TEST first, then applies and saves it (CDS_UPDATEREGISTRY).
    /// </summary>
    public static bool TrySetRefreshRate(string deviceName, int hz)
    {
        var dm = DEVMODE.Create();
        if (!EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref dm)) return false;
        if (dm.dmDisplayFrequency == hz) return true;
        dm.dmSize = (ushort)Marshal.SizeOf<DEVMODE>();
        dm.dmDriverExtra = 0;
        dm.dmDisplayFrequency = (uint)hz;
        dm.dmFields = DM_BITSPERPEL | DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY;

        var test = ChangeDisplaySettingsEx(deviceName, ref dm, IntPtr.Zero, CDS_TEST, IntPtr.Zero);
        if (test != DISP_CHANGE_SUCCESSFUL)
        {
            Log.Warn($"Ecrã {deviceName}: {hz} Hz recusado no teste (código {test}).");
            return false;
        }
        var rc = ChangeDisplaySettingsEx(deviceName, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
        if (rc != DISP_CHANGE_SUCCESSFUL)
        {
            Log.Warn($"Ecrã {deviceName}: falhou a mudança para {hz} Hz (código {rc}).");
            return false;
        }
        Log.Info($"Ecrã {deviceName}: frequência alterada para {hz} Hz.");
        return true;
    }

    private static string? MonitorDeviceString(string adapterDeviceName)
    {
        var mon = DISPLAY_DEVICE.Create();
        return EnumDisplayDevices(adapterDeviceName, 0, ref mon, 0) ? mon.DeviceString : null;
    }

    /// <summary>Maps GDI device names (\\.\DISPLAY1) to EDID friendly names ("LG ULTRAGEAR") via DisplayConfig.</summary>
    private static Dictionary<string, string> FriendlyNames()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out var nPaths, out var nModes) != 0) return map;
            var paths = new DISPLAYCONFIG_PATH_INFO[nPaths];
            var modes = new DISPLAYCONFIG_MODE_INFO[nModes];
            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref nPaths, paths, ref nModes, modes, IntPtr.Zero) != 0) return map;
            for (var i = 0; i < nPaths; i++)
            {
                var p = paths[i];
                var src = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
                        size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                        adapterId = p.sourceInfo.adapterId,
                        id = p.sourceInfo.id,
                    },
                    viewGdiDeviceName = "",
                };
                if (DisplayConfigGetDeviceInfo(ref src) != 0) continue;
                var tgt = new DISPLAYCONFIG_TARGET_DEVICE_NAME
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                        size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                        adapterId = p.targetInfo.adapterId,
                        id = p.targetInfo.id,
                    },
                    monitorFriendlyDeviceName = "",
                    monitorDevicePath = "",
                };
                if (DisplayConfigGetDeviceInfo(ref tgt) != 0) continue;
                if (!string.IsNullOrWhiteSpace(tgt.monitorFriendlyDeviceName) && !map.ContainsKey(src.viewGdiDeviceName))
                    map[src.viewGdiDeviceName] = tgt.monitorFriendlyDeviceName.Trim();
            }
        }
        catch (Exception ex)
        {
            Log.Warn("DisplayConfig: não foi possível obter nomes dos monitores", ex);
        }
        return map;
    }
}
