using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using PcMax.Core.Settings;
using PcMax.Core.Tweaks;
using PcMax.Windows.Tweaks;

namespace PcMax.Windows.SystemServices;

/// <summary>
/// The app runs elevated and trusts its state files (settings, snapshots of tweaks to undo, suspended pids),
/// so the folder that holds them must be writable by administrators only. On every start, before anything
/// reads state, <see cref="Ensure"/> checks <c>%ProgramData%\PcMax</c> and its <c>logs</c> and <c>updates</c>
/// subfolders:
/// <list type="bullet">
/// <item>not a reparse point (junction / symlink),</item>
/// <item>owner is Administrators or SYSTEM,</item>
/// <item>DACL (protected, no inheritance from ProgramData on the root) grants only SYSTEM and Administrators
/// FullControl and Users ReadAndExecute.</item>
/// </list>
/// A folder that fails a check (e.g. pre-created by a normal user, who may create folders in ProgramData)
/// is renamed aside, never trusted, and recreated with that ACL. Afterwards, settings from the old per-user
/// folder (<see cref="AppPaths.LegacyRoot"/>) are migrated once through the normal validating parsers.
/// </summary>
[SupportedOSPlatform("windows")]
public static class SecureDataFolder
{
    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier LocalSystem = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier Users = new(WellKnownSidType.BuiltinUsersSid, null);

    private const FileSystemRights UsersAllowed = FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize;

    /// <summary>
    /// Makes the data folders safe to use, then migrates legacy state. Returns notes for the log (the log itself
    /// lives in this folder, so it cannot be written yet). Throws <see cref="InvalidOperationException"/> when the
    /// folder cannot be made safe: the caller must then not start.
    /// </summary>
    public static IReadOnlyList<string> Ensure(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var notes = new List<string>();
        EnsureDirectory(paths.Root, isRoot: true, notes);
        EnsureDirectory(paths.LogsDir, isRoot: false, notes);
        EnsureDirectory(paths.UpdatesDir, isRoot: false, notes);
        try { MigrateLegacy(paths, AppPaths.LegacyRoot(), notes); }
        catch (Exception ex) { notes.Add("Migração da pasta antiga falhou: " + ex.Message); }
        return notes;
    }

    // ------------------------------------------------------------------ folder checks

    private static void EnsureDirectory(string path, bool isRoot, List<string> notes)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (!Exists(path))
            {
                CreateSecure(path);
                notes.Add($"Pasta criada com permissões só de administrador: {path}");
            }
            var problem = Check(path, isRoot);
            if (problem is null) return;

            notes.Add($"{path} não é segura ({problem}); vai ser posta de parte e recriada.");
            MoveAside(path, notes);
        }
        throw new InvalidOperationException($"Não foi possível proteger a pasta de dados {path}.");
    }

    private static bool Exists(string path) =>
        Directory.Exists(path) || File.Exists(path) || IsReparsePoint(path);

    private static bool IsReparsePoint(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    /// <summary>Returns null when the folder is safe, else what is wrong.</summary>
    internal static string? Check(string path, bool isRoot)
    {
        FileAttributes attrs;
        try { attrs = File.GetAttributes(path); }
        catch (Exception ex) { return "inacessível: " + ex.Message; }
        if ((attrs & FileAttributes.ReparsePoint) != 0) return "é um link ou junction";
        if ((attrs & FileAttributes.Directory) == 0) return "é um ficheiro";

        DirectorySecurity sec;
        try { sec = new DirectoryInfo(path).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access); }
        catch (Exception ex) { return "permissões ilegíveis: " + ex.Message; }

        if (sec.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner
            || !(owner.Equals(Administrators) || owner.Equals(LocalSystem)))
            return "o dono não é Administradores nem SYSTEM";

        // The root must not inherit ProgramData's ACL (which lets Users create files); subfolders may inherit from the root.
        if (isRoot && !sec.AreAccessRulesProtected) return "herda permissões de ProgramData";

        bool adminFull = false, systemFull = false;
        foreach (FileSystemAccessRule rule in sec.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)))
        {
            if (rule.IdentityReference is not SecurityIdentifier sid) return "regra de acesso desconhecida";
            if (rule.AccessControlType != AccessControlType.Allow) return $"regra de negação para {sid}";
            var rights = rule.FileSystemRights;
            if (sid.Equals(Administrators) || sid.Equals(LocalSystem))
            {
                if ((rights & FileSystemRights.FullControl) == FileSystemRights.FullControl && (rule.PropagationFlags & PropagationFlags.InheritOnly) == 0)
                {
                    if (sid.Equals(Administrators)) adminFull = true; else systemFull = true;
                }
                continue;
            }
            if (sid.Equals(Users))
            {
                if ((rights & ~UsersAllowed) != 0) return "Utilizadores podem escrever";
                continue;
            }
            return $"acesso concedido a {sid}";
        }
        if (!adminFull || !systemFull) return "Administradores ou SYSTEM sem controlo total";
        return null;
    }

    private static DirectorySecurity SecureAcl()
    {
        var sec = new DirectorySecurity();
        sec.SetOwner(Administrators);
        sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        sec.AddAccessRule(new FileSystemAccessRule(LocalSystem, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        sec.AddAccessRule(new FileSystemAccessRule(Administrators, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        sec.AddAccessRule(new FileSystemAccessRule(Users, FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
        return sec;
    }

    /// <summary>Creates the folder with the ACL in place from the first moment (no window with a weaker ACL).</summary>
    private static void CreateSecure(string path)
    {
        var di = new DirectoryInfo(path);
        di.Create(SecureAcl());
        // If something already existed (race) or the owner was not applied, set it explicitly on our folder.
        if (Check(path, isRoot: true) is not null && !IsReparsePoint(path))
            di.SetAccessControl(SecureAcl());
    }

    private static void MoveAside(string path, List<string> notes)
    {
        var target = $"{path}.inseguro-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..8]}";
        // Directory.Move / File.Move on a link move the link itself, never its target.
        if ((File.GetAttributes(path) & FileAttributes.Directory) != 0) Directory.Move(path, target);
        else File.Move(path, target);
        notes.Add($"Posta de parte em {target}");
    }

    // ------------------------------------------------------------------ migration

    private static readonly string[] LegacyFiles = { "settings.json", "active-tweaks.json", "suspended.json", "power-scheme.json", "welcome-shown" };

    /// <summary>
    /// One-time copy of the state of versions up to 0.1.x from the per-user folder. That folder is user-writable,
    /// so: nothing is followed through links, files are read with a size cap and only through the same validating
    /// parsers used for our own files, nothing is ever written there, and each file is deleted through a handle
    /// that is first checked to point at the expected path.
    /// </summary>
    internal static void MigrateLegacy(AppPaths paths, string legacyRoot, List<string> notes)
    {
        if (string.Equals(Path.GetFullPath(legacyRoot), Path.GetFullPath(paths.Root), StringComparison.OrdinalIgnoreCase)) return;
        if (!Directory.Exists(legacyRoot)) return;
        if (PathHasReparsePoint(legacyRoot))
        {
            notes.Add("Pasta antiga é (ou está dentro de) um link; não foi migrada.");
            return;
        }

        foreach (var name in LegacyFiles)
        {
            var src = Path.Combine(legacyRoot, name);
            if (!File.Exists(src)) continue;
            try
            {
                var dest = Path.Combine(paths.Root, name);
                if (!File.Exists(dest))
                {
                    var json = AtomicFile.ReadAllTextCapped(src); // refuses links and big files
                    MigrateOne(paths, name, json);
                    notes.Add($"Migrado: {name}");
                }
            }
            catch (Exception ex)
            {
                notes.Add($"{name} da pasta antiga ignorado: {ex.Message}");
            }
            try { DeleteVerified(src); }
            catch (Exception ex) { notes.Add($"Não foi possível apagar {src}: {ex.Message}"); }
        }
    }

    private static void MigrateOne(AppPaths paths, string name, string json)
    {
        switch (name)
        {
            case "settings.json":
                new SettingsStore(paths.SettingsFile).Save(SettingsStore.Parse(json));
                break;
            case "active-tweaks.json":
                // Each snapshot is validated again by its tweak (AcceptsSnapshot) when the engine loads it.
                var snaps = JsonSnapshotStore.Parse(json);
                if (snaps.Count > 0) new JsonSnapshotStore(paths.SnapshotsFile).SaveActive(snaps);
                break;
            case "suspended.json":
                var records = ProcessBooster.Parse(json);
                if (records.Count > 0) ProcessBooster.Save(paths, records);
                break;
            case "power-scheme.json":
                if (PowerPlans.ParseDuplicated(json) is { } g) PowerPlans.SaveDuplicated(paths, g);
                break;
            case "welcome-shown":
                // Only the marker's existence matters (the welcome dialog is not shown again); its content is not copied.
                AtomicFile.WriteAllText(Path.Combine(paths.Root, name), DateTimeOffset.Now.ToString("O"));
                break;
        }
    }

    private static bool PathHasReparsePoint(string dir)
    {
        for (var d = new DirectoryInfo(Path.GetFullPath(dir)); d is not null; d = d.Parent)
        {
            if (d.Parent is null) break; // drive root
            if (d.Exists && (d.Attributes & FileAttributes.ReparsePoint) != 0) return true;
        }
        return false;
    }

    /// <summary>
    /// Deletes <paramref name="path"/> only if a handle opened on it (without following links) resolves to exactly that
    /// path, so a link or junction swapped in at the last moment can never redirect an elevated delete elsewhere.
    /// </summary>
    private static void DeleteVerified(string path)
    {
        using var h = CreateFileW(path, DELETE | FILE_READ_ATTRIBUTES, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
        if (h.IsInvalid) throw new IOException($"Não foi possível abrir (erro {Marshal.GetLastWin32Error()}).");

        var buf = new char[1024];
        var len = GetFinalPathNameByHandleW(h, buf, (uint)buf.Length, 0);
        if (len == 0 || len >= buf.Length) throw new IOException("Caminho final desconhecido.");
        var final = new string(buf, 0, (int)len);
        if (final.StartsWith(@"\\?\UNC\", StringComparison.Ordinal)) throw new IOException("Ficheiro numa partilha de rede.");
        if (final.StartsWith(@"\\?\", StringComparison.Ordinal)) final = final[4..];
        if (!string.Equals(final, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
            throw new IOException($"O ficheiro aponta para outro sítio ({final}); não foi apagado.");

        var info = new FILE_DISPOSITION_INFO { DeleteFile = 1 };
        if (!SetFileInformationByHandle(h, FileDispositionInfo, ref info, (uint)Marshal.SizeOf<FILE_DISPOSITION_INFO>()))
            throw new IOException($"Apagar falhou (erro {Marshal.GetLastWin32Error()}).");
    }

    private const uint DELETE = 0x00010000;
    private const uint FILE_READ_ATTRIBUTES = 0x0080;
    private const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, FILE_SHARE_DELETE = 4;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    private const int FileDispositionInfo = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct FILE_DISPOSITION_INFO { public byte DeleteFile; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, [Out] char[] filePath, uint filePathSize, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle file, int fileInformationClass, ref FILE_DISPOSITION_INFO info, uint bufferSize);
}
