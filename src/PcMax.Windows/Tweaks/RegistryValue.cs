using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Win32;

namespace PcMax.Windows.Tweaks;

/// <summary>
/// Reads/writes single registry values and serialises their exact previous state into a string,
/// so a tweak can restore it later. "Absent" is stored as <see cref="Absent"/>, which is distinct from
/// an empty REG_SZ ("sz:"). Restoring an absent value deletes it.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class RegistryValue
{
    public const string Absent = "<absent>";

    /// <summary>Snapshot key for a value: "HKCU\Software\...|ValueName".</summary>
    public static string Key(RegistryHive hive, string subKey, string name) => $"{HiveName(hive)}\\{subKey}|{name}";

    public static object? Read(RegistryHive hive, string subKey, string name)
    {
        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var k = root.OpenSubKey(subKey, writable: false);
        return k?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
    }

    public static int? ReadDword(RegistryHive hive, string subKey, string name) =>
        Read(hive, subKey, name) switch
        {
            int i => i,
            long l => (int)l,
            _ => null,
        };

    /// <summary>Serialises the current state of a value (type + data, or <see cref="Absent"/>).</summary>
    public static string Capture(RegistryHive hive, string subKey, string name)
    {
        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var k = root.OpenSubKey(subKey, writable: false);
        if (k is null) return Absent;
        var v = k.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (v is null) return Absent;
        var kind = k.GetValueKind(name);
        return kind switch
        {
            RegistryValueKind.DWord => "dword:" + unchecked((uint)(int)v).ToString(CultureInfo.InvariantCulture),
            RegistryValueKind.QWord => "qword:" + unchecked((ulong)(long)v).ToString(CultureInfo.InvariantCulture),
            RegistryValueKind.String => "sz:" + (string)v,
            RegistryValueKind.ExpandString => "expandsz:" + (string)v,
            RegistryValueKind.MultiString => "multisz:" + JsonSerializer.Serialize((string[])v),
            RegistryValueKind.Binary => "binary:" + Convert.ToBase64String((byte[])v),
            _ => "binary:" + Convert.ToBase64String(v as byte[] ?? Array.Empty<byte>()),
        };
    }

    public static void SetDword(RegistryHive hive, string subKey, string name, int value)
    {
        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var k = root.CreateSubKey(subKey, writable: true)
            ?? throw new InvalidOperationException($"Não foi possível abrir {HiveName(hive)}\\{subKey}.");
        k.SetValue(name, value, RegistryValueKind.DWord);
    }

    /// <summary>Restores a value captured with <see cref="Capture"/>. Absent (or null) deletes the value.</summary>
    public static void Restore(RegistryHive hive, string subKey, string name, string? captured)
    {
        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        if (captured is null || captured == Absent)
        {
            using var existing = root.OpenSubKey(subKey, writable: true);
            existing?.DeleteValue(name, throwOnMissingValue: false);
            return;
        }

        var sep = captured.IndexOf(':');
        if (sep <= 0) throw new FormatException($"Snapshot de registo inválido para {name}.");
        var type = captured[..sep];
        var data = captured[(sep + 1)..];
        using var k = root.CreateSubKey(subKey, writable: true)
            ?? throw new InvalidOperationException($"Não foi possível abrir {HiveName(hive)}\\{subKey}.");
        switch (type)
        {
            case "dword":
                k.SetValue(name, unchecked((int)uint.Parse(data, CultureInfo.InvariantCulture)), RegistryValueKind.DWord);
                break;
            case "qword":
                k.SetValue(name, unchecked((long)ulong.Parse(data, CultureInfo.InvariantCulture)), RegistryValueKind.QWord);
                break;
            case "sz":
                k.SetValue(name, data, RegistryValueKind.String);
                break;
            case "expandsz":
                k.SetValue(name, data, RegistryValueKind.ExpandString);
                break;
            case "multisz":
                k.SetValue(name, JsonSerializer.Deserialize<string[]>(data) ?? Array.Empty<string>(), RegistryValueKind.MultiString);
                break;
            case "binary":
                k.SetValue(name, Convert.FromBase64String(data), RegistryValueKind.Binary);
                break;
            default:
                throw new FormatException($"Tipo de registo desconhecido '{type}' para {name}.");
        }
    }

    private static string HiveName(RegistryHive hive) => hive switch
    {
        RegistryHive.CurrentUser => "HKCU",
        RegistryHive.LocalMachine => "HKLM",
        _ => hive.ToString(),
    };
}
