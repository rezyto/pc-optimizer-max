using System.Runtime.Versioning;

namespace PcMax.Windows.SystemServices;

/// <summary>Where the per-machine installer puts the app: %ProgramFiles%\PC Max (admin-only).</summary>
[SupportedOSPlatform("windows")]
public static class InstallLocation
{
    /// <summary>
    /// True when <paramref name="path"/> is inside %ProgramFiles% (64-bit), a folder only administrators can change.
    /// Dev builds and copies elsewhere return false.
    /// </summary>
    public static bool IsUnderProgramFiles(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolderOption.DoNotVerify);
            if (string.IsNullOrEmpty(pf)) return false;
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(pf)) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(path);
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && !full.Contains("..", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The running PcMax.exe is the installed copy.</summary>
    public static bool IsInstalledCopy() => IsUnderProgramFiles(Environment.ProcessPath);
}
