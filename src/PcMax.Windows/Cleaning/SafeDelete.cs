using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace PcMax.Windows.Cleaning;

/// <summary>Paths resolved from sources a non-admin user cannot redirect.</summary>
[SupportedOSPlatform("windows")]
internal static class TrustedPaths
{
    private static readonly Lazy<string?> _local = new(ResolveLocalAppData);
    public static string? LocalAppData => _local.Value;

    private static string? ResolveLocalAppData()
    {
        try
        {
            var sid = WindowsIdentity.GetCurrent().User?.Value;
            if (sid is null) return null;
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\" + sid);
            var raw = key?.GetValue("ProfileImagePath", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            if (string.IsNullOrWhiteSpace(raw)) return null;
            // Expand only %SystemDrive%/%SystemRoot%, from GetWindowsDirectory rather than the environment.
            var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var drive = Path.GetPathRoot(win)!.TrimEnd('\\');
            var profile = raw.Replace("%SystemDrive%", drive, StringComparison.OrdinalIgnoreCase)
                             .Replace("%SystemRoot%", win, StringComparison.OrdinalIgnoreCase);
            if (profile.Contains('%') || !Path.IsPathFullyQualified(profile)) return null;
            return Path.Combine(profile, "AppData", "Local");
        }
        catch { return null; }
    }
}

/// <summary>
/// Deletes a file or empty folder by handle after proving, from the handle itself, that it is a
/// regular item inside the expected root. Links are never followed or deleted.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class SafeDelete
{
    private const uint DELETE = 0x00010000;
    private const uint FILE_READ_ATTRIBUTES = 0x80;
    private const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10, FILE_ATTRIBUTE_REPARSE_POINT = 0x400;
    private const int FileBasicInfo = 0, FileAttributeTagInfo = 9, FileDispositionInfo = 4, FileDispositionInfoEx = 21;
    private const uint FILE_DISPOSITION_FLAG_DELETE = 0x1, FILE_DISPOSITION_FLAG_POSIX_SEMANTICS = 0x2,
        FILE_DISPOSITION_FLAG_IGNORE_READONLY_ATTRIBUTE = 0x10;

    [StructLayout(LayoutKind.Sequential)] private struct FILE_ATTRIBUTE_TAG_INFO { public uint FileAttributes; public uint ReparseTag; }
    [StructLayout(LayoutKind.Sequential)] private struct FILE_DISPOSITION_INFO_EX { public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct FILE_DISPOSITION_INFO { [MarshalAs(UnmanagedType.U1)] public bool DeleteFile; }
    [StructLayout(LayoutKind.Sequential)]
    private struct FILE_BASIC_INFO { public long CreationTime, LastAccessTime, LastWriteTime, ChangeTime; public uint FileAttributes; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr sa, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle h, StringBuilder buf, uint len, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle h, int cls, out FILE_ATTRIBUTE_TAG_INFO info, uint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle h, int cls, out FILE_BASIC_INFO info, uint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle h, int cls, ref FILE_DISPOSITION_INFO_EX info, uint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle h, int cls, ref FILE_DISPOSITION_INFO info, uint size);

    /// <summary>Opens the root without delete sharing (so it cannot be renamed or replaced) and returns its real path.</summary>
    public static SafeFileHandle? OpenRoot(string root, out string? finalPath)
    {
        finalPath = null;
        var h = CreateFileW(root, FILE_READ_ATTRIBUTES, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);
        if (h.IsInvalid) { h.Dispose(); return null; }
        if (!IsPlain(h, expectDirectory: true)) { h.Dispose(); return null; }
        finalPath = FinalPath(h);
        if (finalPath is null) { h.Dispose(); return null; }
        return h;
    }

    public static bool TryDelete(string path, string rootFinal, bool isDirectory, TimeSpan minAge)
    {
        try
        {
            // No FILE_SHARE_DELETE: while we hold it, nobody can rename or move this item.
            using var h = CreateFileW(path, DELETE | FILE_READ_ATTRIBUTES, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero,
                OPEN_EXISTING, FILE_FLAG_OPEN_REPARSE_POINT | (isDirectory ? FILE_FLAG_BACKUP_SEMANTICS : 0), IntPtr.Zero);
            if (h.IsInvalid) return false;
            if (!IsPlain(h, isDirectory)) return false;

            var final = FinalPath(h);
            var prefix = rootFinal.TrimEnd('\\') + "\\";
            if (final is null || !final.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

            if (minAge > TimeSpan.Zero)
            {
                if (!GetFileInformationByHandleEx(h, FileBasicInfo, out FILE_BASIC_INFO b, (uint)Marshal.SizeOf<FILE_BASIC_INFO>())) return false;
                if (DateTime.FromFileTimeUtc(b.LastWriteTime) > DateTime.UtcNow - minAge) return false;
            }

            var ex = new FILE_DISPOSITION_INFO_EX
            {
                Flags = FILE_DISPOSITION_FLAG_DELETE | FILE_DISPOSITION_FLAG_POSIX_SEMANTICS | FILE_DISPOSITION_FLAG_IGNORE_READONLY_ATTRIBUTE,
            };
            if (SetFileInformationByHandle(h, FileDispositionInfoEx, ref ex, (uint)Marshal.SizeOf<FILE_DISPOSITION_INFO_EX>()))
                return true;
            // Older Windows 10 without the Ex class.
            var basic = new FILE_DISPOSITION_INFO { DeleteFile = true };
            return SetFileInformationByHandle(h, FileDispositionInfo, ref basic, (uint)Marshal.SizeOf<FILE_DISPOSITION_INFO>());
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPlain(SafeFileHandle h, bool expectDirectory)
    {
        if (!GetFileInformationByHandleEx(h, FileAttributeTagInfo, out FILE_ATTRIBUTE_TAG_INFO tag, (uint)Marshal.SizeOf<FILE_ATTRIBUTE_TAG_INFO>()))
            return false;
        if ((tag.FileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0) return false;
        return ((tag.FileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0) == expectDirectory;
    }

    private static string? FinalPath(SafeFileHandle h)
    {
        var sb = new StringBuilder(1024);
        var n = GetFinalPathNameByHandleW(h, sb, (uint)sb.Capacity, 0);
        if (n == 0) return null;
        if (n > sb.Capacity)
        {
            sb = new StringBuilder((int)n + 1);
            n = GetFinalPathNameByHandleW(h, sb, (uint)sb.Capacity, 0);
            if (n == 0 || n > sb.Capacity) return null;
        }
        return sb.ToString();
    }
}
