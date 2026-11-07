using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using PcMax.Core.Settings;
using PcMax.Windows.Native;

namespace PcMax.Windows.Tweaks;

/// <summary>Power scheme helpers (powrprof.dll). powercfg.exe is only used to duplicate a scheme.</summary>
[SupportedOSPlatform("windows")]
internal static partial class PowerPlans
{
    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid UltimatePerformance = new("e9a42b02-d5df-448d-aa00-03f14749eb61");

    // "Power scheme personality" setting: 0 = power saver, 1 = high performance, 2 = balanced.
    private static readonly Guid NoSubgroup = new("fea3413e-7e05-4911-9a71-700331f1c294");
    private static readonly Guid PersonalitySetting = new("245d8541-3943-4422-b025-13a784f679b7");

    private static readonly object FileGate = new();

    public static Guid? GetActive()
    {
        if (NativeMethods.PowerGetActiveScheme(IntPtr.Zero, out var ptr) != NativeMethods.ERROR_SUCCESS || ptr == IntPtr.Zero)
            return null;
        try { return Marshal.PtrToStructure<Guid>(ptr); }
        finally { NativeMethods.LocalFree(ptr); }
    }

    public static void SetActive(Guid scheme)
    {
        var g = scheme;
        var rc = NativeMethods.PowerSetActiveScheme(IntPtr.Zero, ref g);
        if (rc != NativeMethods.ERROR_SUCCESS)
            throw new InvalidOperationException($"PowerSetActiveScheme falhou (código {rc}).");
    }

    public static string? GetFriendlyName(Guid scheme)
    {
        var g = scheme;
        uint size = 0;
        var rc = NativeMethods.PowerReadFriendlyName(IntPtr.Zero, ref g, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref size);
        if ((rc != NativeMethods.ERROR_SUCCESS && rc != NativeMethods.ERROR_MORE_DATA) || size == 0) return null;
        var buf = Marshal.AllocHGlobal((int)size);
        try
        {
            rc = NativeMethods.PowerReadFriendlyName(IntPtr.Zero, ref g, IntPtr.Zero, IntPtr.Zero, buf, ref size);
            return rc == NativeMethods.ERROR_SUCCESS ? Marshal.PtrToStringUni(buf)?.TrimEnd('\0') : null;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    /// <summary>All power schemes installed on this PC.</summary>
    public static IReadOnlyList<Guid> List()
    {
        var result = new List<Guid>();
        for (uint i = 0; i < 256; i++)
        {
            var g = Guid.Empty;
            uint size = (uint)Marshal.SizeOf<Guid>();
            var rc = NativeMethods.PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, NativeMethods.ACCESS_SCHEME, i, ref g, ref size);
            if (rc == NativeMethods.ERROR_NO_MORE_ITEMS) break;
            if (rc != NativeMethods.ERROR_SUCCESS) break;
            result.Add(g);
        }
        return result;
    }

    public static bool Exists(Guid scheme) => List().Contains(scheme);

    /// <summary>1 = high performance personality (also used by Ultimate and its copies).</summary>
    public static uint? GetPersonality(Guid scheme)
    {
        var s = scheme;
        var sub = NoSubgroup;
        var set = PersonalitySetting;
        return NativeMethods.PowerReadACValueIndex(IntPtr.Zero, ref s, ref sub, ref set, out var v) == NativeMethods.ERROR_SUCCESS ? v : null;
    }

    public static bool IsHighPerformance(Guid scheme, AppPaths? paths)
    {
        if (scheme == HighPerformance || scheme == UltimatePerformance) return true;
        if (paths is not null && LoadDuplicated(paths) == scheme) return true;
        return GetPersonality(scheme) == 1;
    }

    /// <summary>Runs "powercfg -duplicatescheme &lt;source&gt;" and returns the new scheme GUID.</summary>
    public static Guid? Duplicate(Guid source)
    {
        var exe = Path.Combine(Environment.SystemDirectory, "powercfg.exe");
        if (!File.Exists(exe)) return null;
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("-duplicatescheme");
        psi.ArgumentList.Add(source.ToString("D"));
        using var p = Process.Start(psi);
        if (p is null) return null;
        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(15000))
        {
            try { p.Kill(); } catch { }
            return null;
        }
        var output = stdoutTask.Result + stderrTask.Result;
        if (p.ExitCode != 0) { Log.Warn($"powercfg -duplicatescheme {source} saiu com {p.ExitCode}: {output.Trim()}"); return null; }
        // Output is localised ("Power Scheme GUID: <guid>  (Name)"); the only GUID in it is the new one.
        var m = GuidRegex().Match(output);
        return m.Success && Guid.TryParse(m.Value, out var g) && g != source ? g : null;
    }

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidRegex();

    // The scheme we created by duplicating, so we reuse it instead of creating a new copy every game.
    private sealed record State(Guid? DuplicatedScheme);

    private static string StateFile(AppPaths paths) => Path.Combine(paths.Root, "power-scheme.json");

    public static Guid? LoadDuplicated(AppPaths paths)
    {
        lock (FileGate)
        {
            try
            {
                var f = StateFile(paths);
                if (!File.Exists(f)) return null;
                return JsonSerializer.Deserialize<State>(File.ReadAllText(f), JsonDefaults.Options)?.DuplicatedScheme;
            }
            catch (Exception ex)
            {
                Log.Warn("Não foi possível ler power-scheme.json", ex);
                return null;
            }
        }
    }

    public static void SaveDuplicated(AppPaths paths, Guid scheme)
    {
        lock (FileGate)
        {
            try { AtomicFile.WriteAllText(StateFile(paths), JsonSerializer.Serialize(new State(scheme), JsonDefaults.Options)); }
            catch (Exception ex) { Log.Warn("Não foi possível guardar power-scheme.json", ex); }
        }
    }
}
