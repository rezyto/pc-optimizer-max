using System.Globalization;
using System.Text.RegularExpressions;

namespace PcMax.Core.Updates;

/// <summary>A release version "MAJOR.MINOR.PATCH" (optionally with a leading "v"). Pre-releases are not accepted.</summary>
public readonly partial record struct ReleaseVersion(int Major, int Minor, int Patch) : IComparable<ReleaseVersion>
{
    public static bool TryParse(string? text, out ReleaseVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var m = VersionRegex().Match(text.Trim());
        if (!m.Success) return false;
        if (!int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var a)
            || !int.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var b)
            || !int.TryParse(m.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var c))
            return false;
        version = new ReleaseVersion(a, b, c);
        return true;
    }

    public int CompareTo(ReleaseVersion other)
    {
        var c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        return c != 0 ? c : Patch.CompareTo(other.Patch);
    }

    public static bool operator >(ReleaseVersion a, ReleaseVersion b) => a.CompareTo(b) > 0;
    public static bool operator <(ReleaseVersion a, ReleaseVersion b) => a.CompareTo(b) < 0;
    public static bool operator >=(ReleaseVersion a, ReleaseVersion b) => a.CompareTo(b) >= 0;
    public static bool operator <=(ReleaseVersion a, ReleaseVersion b) => a.CompareTo(b) <= 0;

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");

    // Up to 6 digits per part keeps int parsing safe; no pre-release / build suffix.
    [GeneratedRegex(@"^v?(\d{1,6})\.(\d{1,6})\.(\d{1,6})$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionRegex();
}

/// <summary>Naming and checksum rules shared by the release workflow and the in-app updater.</summary>
public static partial class ReleaseAssets
{
    public static string InstallerName(ReleaseVersion v) => $"PcMax-Setup-{v}.exe";
    public static string ChecksumName(ReleaseVersion v) => InstallerName(v) + ".sha256";

    /// <summary>
    /// Parses a ".sha256" file: either just the hex digest or "digest  filename" (sha256sum format).
    /// When a file name is present it must match <paramref name="expectedFileName"/>. Returns the lowercase digest.
    /// </summary>
    public static string? ParseChecksum(string? content, string expectedFileName)
    {
        if (string.IsNullOrWhiteSpace(content) || content.Length > 4096) return null;
        var line = content.Trim().Split('\n')[0].Trim();
        var m = ChecksumRegex().Match(line);
        if (!m.Success) return null;
        var name = m.Groups[2].Value.Trim();
        if (name.StartsWith('*')) name = name[1..];
        if (name.Length > 0 && !string.Equals(name, expectedFileName, StringComparison.OrdinalIgnoreCase)) return null;
        return m.Groups[1].Value.ToLowerInvariant();
    }

    [GeneratedRegex(@"^([0-9a-fA-F]{64})(?:\s+(.+))?$", RegexOptions.CultureInvariant)]
    private static partial Regex ChecksumRegex();
}
