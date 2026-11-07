using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;
using PcMax.Core.Platform;

namespace PcMax.Windows.Startup;

/// <summary>
/// Lists programs that start with Windows and enables/disables them exactly like Task Manager:
/// through the Explorer\StartupApproved keys (first byte even = enabled, odd = disabled, followed by
/// a FILETIME). The Run values and startup-folder files themselves are never modified or deleted.
/// Ids are "&lt;source&gt;:&lt;name&gt;" and are only accepted if they match an entry that exists right now.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class StartupManager : IStartupManager
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedRoot = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

    private sealed record Source(string Id, string Label, RegistryHive ApprovedHive, string ApprovedKey,
        RegistryHive? RunHive = null, RegistryView RunView = RegistryView.Registry64, Func<string>? Folder = null);

    private static readonly Source[] Sources =
    {
        new("hkcu-run", "Registo (utilizador)", RegistryHive.CurrentUser, ApprovedRoot + @"\Run", RegistryHive.CurrentUser),
        new("hklm-run", "Registo (todos os utilizadores)", RegistryHive.LocalMachine, ApprovedRoot + @"\Run", RegistryHive.LocalMachine),
        new("hklm-run32", "Registo (todos os utilizadores, 32 bits)", RegistryHive.LocalMachine, ApprovedRoot + @"\Run32",
            RegistryHive.LocalMachine, RegistryView.Registry32),
        new("user-folder", "Pasta Arranque (utilizador)", RegistryHive.CurrentUser, ApprovedRoot + @"\StartupFolder",
            Folder: () => Environment.GetFolderPath(Environment.SpecialFolder.Startup)),
        new("common-folder", "Pasta Arranque (todos os utilizadores)", RegistryHive.LocalMachine, ApprovedRoot + @"\StartupFolder",
            Folder: () => Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)),
    };

    private sealed record Entry(Source Source, string ValueName, string Display, string Command);

    public IReadOnlyList<StartupItem> List()
    {
        var items = new List<StartupItem>();
        foreach (var e in Enumerate())
        {
            bool enabled = true;
            try { enabled = IsEnabled(e); } catch (Exception ex) { Log.Warn($"Arranque: estado de {e.ValueName} ilegível", ex); }
            items.Add(new StartupItem($"{e.Source.Id}:{e.ValueName}", e.Display, e.Command, e.Source.Label, enabled, Publisher(e.Command)));
        }
        return items;
    }

    public void SetEnabled(string id, bool enabled)
    {
        var entry = Resolve(id) ?? throw new ArgumentException("Programa de arranque desconhecido.", nameof(id));
        using var root = RegistryKey.OpenBaseKey(entry.Source.ApprovedHive, RegistryView.Registry64);
        using var k = root.CreateSubKey(entry.Source.ApprovedKey, writable: true)
            ?? throw new InvalidOperationException("Não foi possível abrir a chave StartupApproved.");
        var data = new byte[12];
        data[0] = enabled ? (byte)0x02 : (byte)0x03;
        if (!enabled)
        {
            // Task Manager stores the time it was disabled ("Disabled on" column).
            BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
        }
        k.SetValue(entry.ValueName, data, RegistryValueKind.Binary);
        Log.Info($"Arranque: {entry.Display} {(enabled ? "ativado" : "desativado")}");
    }

    /// <summary>Validates an id against the current entries; the value name written is always one we enumerated.</summary>
    private Entry? Resolve(string id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 600) return null;
        var sep = id.IndexOf(':');
        if (sep <= 0) return null;
        var sourceId = id[..sep];
        var name = id[(sep + 1)..];
        if (name.Length == 0 || name.Any(char.IsControl)) return null;
        var source = Sources.FirstOrDefault(s => s.Id == sourceId);
        if (source is null) return null;
        if (source.Folder is not null && (name.Contains('\\') || name.Contains('/') || name.Contains("..") || Path.GetFileName(name) != name))
            return null;
        return Enumerate(source).FirstOrDefault(e => string.Equals(e.ValueName, name, StringComparison.Ordinal));
    }

    private static IEnumerable<Entry> Enumerate() => Sources.SelectMany(Enumerate);

    private static IEnumerable<Entry> Enumerate(Source s)
    {
        var list = new List<Entry>();
        try
        {
            if (s.RunHive is { } hive)
            {
                using var root = RegistryKey.OpenBaseKey(hive, s.RunView);
                using var k = root.OpenSubKey(RunPath, writable: false);
                if (k is null) return list;
                foreach (var name in k.GetValueNames())
                {
                    if (string.IsNullOrEmpty(name)) continue; // "(Default)" is not a startup entry
                    var cmd = k.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? "";
                    list.Add(new Entry(s, name, name, cmd));
                }
            }
            else if (s.Folder is not null)
            {
                var dir = s.Folder();
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return list;
                var di = new DirectoryInfo(dir);
                if ((di.Attributes & FileAttributes.ReparsePoint) != 0) return list;
                foreach (var f in di.EnumerateFiles("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 }))
                {
                    if (f.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    list.Add(new Entry(s, f.Name, Path.GetFileNameWithoutExtension(f.Name), f.FullName));
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Arranque: não foi possível ler {s.Id}", ex);
        }
        return list;
    }

    private static bool IsEnabled(Entry e)
    {
        using var root = RegistryKey.OpenBaseKey(e.Source.ApprovedHive, RegistryView.Registry64);
        using var k = root.OpenSubKey(e.Source.ApprovedKey, writable: false);
        // No StartupApproved value = enabled (never toggled). Odd first byte (03, 07…) = disabled.
        return k?.GetValue(e.ValueName) is not byte[] { Length: > 0 } b || (b[0] & 1) == 0;
    }

    private static string? Publisher(string command)
    {
        try
        {
            var path = ExecutablePath(command);
            if (path is null || !File.Exists(path)) return null;
            if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
            var company = FileVersionInfo.GetVersionInfo(path).CompanyName;
            return string.IsNullOrWhiteSpace(company) ? null : company.Trim();
        }
        catch { return null; }
    }

    internal static string? ExecutablePath(string command)
    {
        var c = Environment.ExpandEnvironmentVariables(command.Trim());
        if (c.Length == 0) return null;
        if (c[0] == '"')
        {
            var end = c.IndexOf('"', 1);
            return end > 1 ? c[1..end] : null;
        }
        var exe = c.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exe > 0) return c[..(exe + 4)];
        var space = c.IndexOf(' ');
        return space > 0 ? c[..space] : c;
    }
}
