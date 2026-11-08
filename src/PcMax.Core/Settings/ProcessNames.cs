using System.Text.RegularExpressions;

namespace PcMax.Core.Settings;

/// <summary>Validation of process / executable names typed by the user or read from settings.json.</summary>
public static partial class ProcessNames
{
    public const int MaxLength = 100;

    /// <summary>
    /// A plain process name without ".exe": letters, digits, space, dot, underscore, plus and hyphen.
    /// No path separators, wildcards, quotes or control characters.
    /// </summary>
    public static bool IsValid(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Length <= MaxLength && name.Trim() == name && NameRegex().IsMatch(name)
        && name != "." && name != "..";

    /// <summary>Trims and strips a trailing ".exe"; returns null if the result is not a valid name.</summary>
    public static string? Clean(string? name)
    {
        var n = (name ?? "").Trim();
        if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = n[..^4].TrimEnd();
        return IsValid(n) ? n : null;
    }

    [GeneratedRegex(@"^[\p{L}\p{Nd} ._+\-'()]+$")]
    private static partial Regex NameRegex();
}
