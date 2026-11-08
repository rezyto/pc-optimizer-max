using System.IO.Enumeration;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PcMax.Core.Platform;
using static PcMax.Windows.Native.NativeMethods;

namespace PcMax.Windows.Cleaning;

/// <summary>
/// Disk cleanup. Safety rules, applied to every file and folder:
/// - reparse points (symlinks, junctions, mount points) are never followed nor deleted;
/// - nothing outside the category root is touched: each item is opened without following links,
///   its real path (from the handle) is checked against the root's real path, and it is deleted
///   through that same handle, so a folder swapped for a junction mid-run cannot redirect a delete;
/// - roots come from trusted sources (HKLM profile path, GetWindowsDirectory), not TMP/TEMP;
/// - recursion depth is capped; files in use or without permission are skipped;
/// - temp-like folders only lose files older than 24 h.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class Cleaner : ICleaner
{
    private const int MaxDepth = 24;
    private const string RecycleBinId = "recycle-bin";

    private sealed record Category(string Id, string Name, string Description, bool SafeByDefault,
        TimeSpan MinAge, Func<IEnumerable<string>> Roots, string Pattern = "*", bool Recursive = true);

    // Roots come from sources a normal user cannot redirect: the profile path in HKLM (not the
    // TMP/TEMP variables or HKCU shell folders) and GetWindowsDirectory.
    private static string Local => TrustedPaths.LocalAppData ?? "";
    private static string WinDir => Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static string ProgramData => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

    private static readonly TimeSpan Day = TimeSpan.FromHours(24);

    private static readonly Category[] Categories =
    {
        new("user-temp", "Ficheiros temporários (utilizador)",
            "Ficheiros que os programas deixam na tua pasta temporária. Só são apagados os que têm mais de 24 horas.",
            true, Day, () => TrustedPaths.LocalAppData is { } l ? new[] { Path.Combine(l, "Temp") } : Array.Empty<string>()),
        new("windows-temp", "Ficheiros temporários do Windows",
            "Ficheiros temporários do próprio Windows e de instaladores. Só são apagados os que têm mais de 24 horas.",
            false, Day, () => new[] { Path.Combine(WinDir, "Temp") }),
        new("windows-update-cache", "Cache do Windows Update",
            "Atualizações já descarregadas pelo Windows. Pode libertar vários GB, mas se houver uma atualização pendente, o Windows volta a descarregá-la.",
            false, Day, () => new[] { Path.Combine(WinDir, "SoftwareDistribution", "Download") }),
        new("dx-shader-cache", "Cache de shaders do DirectX",
            "Shaders já compilados pelo Windows. Só vale a pena limpar se tiveres problemas gráficos ou depois de atualizar o driver: " +
            "os jogos voltam a compilá-los e podem ter pequenos travões nos primeiros minutos.",
            false, TimeSpan.Zero, () => new[] { Path.Combine(Local, "D3DSCache") }),
        new("nvidia-shader-cache", "Cache de shaders NVIDIA",
            "Shaders já compilados pelo driver NVIDIA. Limpa só se tiveres problemas gráficos ou depois de atualizar o driver: " +
            "os jogos voltam a compilá-los e podem ter pequenos travões nos primeiros minutos.",
            false, TimeSpan.Zero, () => new[] { Path.Combine(Local, "NVIDIA", "DXCache"), Path.Combine(Local, "NVIDIA", "GLCache") }),
        new("amd-shader-cache", "Cache de shaders AMD",
            "Shaders já compilados pelo driver AMD. Limpa só se tiveres problemas gráficos ou depois de atualizar o driver: " +
            "os jogos voltam a compilá-los e podem ter pequenos travões nos primeiros minutos.",
            false, TimeSpan.Zero, () => new[] { Path.Combine(Local, "AMD", "DxCache") }),
        new("thumbnail-cache", "Miniaturas do Explorador",
            "Pré-visualizações de imagens e vídeos. O Windows volta a criá-las, por isso as pastas com muitas fotos ficam mais lentas a abrir da próxima vez.",
            false, TimeSpan.Zero, () => new[] { Path.Combine(Local, "Microsoft", "Windows", "Explorer") }, "thumbcache_*.db", Recursive: false),
        new("crash-dumps", "Relatórios de erros e despejos de memória",
            "Ficheiros criados quando um programa ou o Windows falha. Só servem para diagnóstico e podem ocupar muito espaço. Só são apagados os que têm mais de 24 horas.",
            false, Day, () => new[]
            {
                Path.Combine(Local, "CrashDumps"),
                Path.Combine(WinDir, "Minidump"),
                Path.Combine(ProgramData, "Microsoft", "Windows", "WER", "ReportArchive"),
                Path.Combine(ProgramData, "Microsoft", "Windows", "WER", "ReportQueue"),
            }),
    };

    public Task<IReadOnlyList<CleanupCategory>> ScanAsync(CancellationToken ct = default) => Task.Run<IReadOnlyList<CleanupCategory>>(() =>
    {
        var result = new List<CleanupCategory>();
        foreach (var c in Categories)
        {
            ct.ThrowIfCancellationRequested();
            long bytes = 0;
            int files = 0;
            foreach (var f in Candidates(c, SafeRoots(c), ct)) { bytes += f.Length; files++; }
            result.Add(new CleanupCategory(c.Id, c.Name, c.Description, bytes, files, c.SafeByDefault));
        }
        var (rbBytes, rbItems) = QueryRecycleBin();
        result.Add(new CleanupCategory(RecycleBinId, "Reciclagem",
            "Ficheiros que apagaste e estão na Reciclagem. Depois de esvaziar, não é possível recuperá-los.",
            rbBytes, (int)Math.Min(rbItems, int.MaxValue), false));
        return result;
    }, ct);

    public Task<long> CleanAsync(IEnumerable<string> categoryIds, CancellationToken ct = default)
    {
        var ids = categoryIds.ToHashSet(StringComparer.Ordinal);
        return Task.Run(() =>
        {
            long freed = 0;
            foreach (var c in Categories.Where(c => ids.Contains(c.Id)))
            {
                ct.ThrowIfCancellationRequested();
                long catFreed = 0;
                foreach (var root in SafeRoots(c))
                {
                    // Hold the root open (no delete sharing) so it cannot be swapped during the run,
                    // and resolve its real path once.
                    using var rootLock = SafeDelete.OpenRoot(root, out var rootFinal);
                    if (rootLock is null || rootFinal is null) continue;
                    foreach (var f in Candidates(c, new[] { root }, ct).ToList())
                    {
                        ct.ThrowIfCancellationRequested();
                        if (SafeDelete.TryDelete(f.FullName, rootFinal, isDirectory: false, c.MinAge)) catFreed += f.Length;
                    }
                    if (c.Recursive) RemoveEmptyDirectories(c, root, rootFinal, ct);
                }
                Log.Info($"Limpeza {c.Id}: {catFreed / 1024.0 / 1024:0.0} MB libertados");
                freed += catFreed;
            }
            if (ids.Contains(RecycleBinId)) freed += EmptyRecycleBin();
            return freed;
        }, ct);
    }

    // ------------------------------------------------------------------ file walking

    /// <summary>Files eligible for deletion in a category (never via a reparse point, never outside the root).</summary>
    private static IEnumerable<FileInfo> Candidates(Category c, IEnumerable<string> roots, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow - c.MinAge;
        foreach (var rootPath in roots)
        {
            var rootPrefix = rootPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var stack = new Stack<(DirectoryInfo Dir, int Depth)>();
            stack.Push((new DirectoryInfo(rootPath), 0));
            while (stack.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var (dir, depth) = stack.Pop();
                IEnumerable<FileSystemInfo> entries;
                try
                {
                    entries = dir.EnumerateFileSystemInfos("*", new EnumerationOptions
                    {
                        IgnoreInaccessible = true,
                        RecurseSubdirectories = false,
                        AttributesToSkip = 0,
                        ReturnSpecialDirectories = false,
                    }).ToList();
                }
                catch { continue; }

                foreach (var e in entries)
                {
                    FileAttributes attrs;
                    try { attrs = e.Attributes; } catch { continue; }
                    if ((attrs & FileAttributes.ReparsePoint) != 0) continue; // never follow or delete links
                    if (!IsInside(e.FullName, rootPrefix)) continue;

                    if (e is DirectoryInfo sub)
                    {
                        if (c.Recursive && depth + 1 < MaxDepth) stack.Push((sub, depth + 1));
                        continue;
                    }
                    if (e is not FileInfo f) continue;
                    if (c.Pattern != "*" && !FileSystemName.MatchesWin32Expression(c.Pattern, f.Name)) continue;
                    DateTime written;
                    try { written = f.LastWriteTimeUtc; } catch { continue; }
                    if (c.MinAge > TimeSpan.Zero && written > cutoff) continue;
                    yield return f;
                }
            }
        }
    }

    /// <summary>Category roots that exist, are absolute, and are not themselves reparse points.</summary>
    private static IEnumerable<string> SafeRoots(Category c)
    {
        IEnumerable<string> roots;
        try { roots = c.Roots().ToList(); } catch { yield break; }
        foreach (var r in roots)
        {
            if (string.IsNullOrWhiteSpace(r) || !Path.IsPathFullyQualified(r)) continue;
            string full;
            try { full = Path.GetFullPath(r); } catch { continue; }
            // Never clean a drive root or the Windows folder itself by mistake.
            if (Path.GetPathRoot(full)?.TrimEnd('\\').Length >= full.TrimEnd('\\').Length) continue;
            if (string.Equals(full.TrimEnd('\\'), WinDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var di = new DirectoryInfo(full);
                if (!di.Exists || (di.Attributes & FileAttributes.ReparsePoint) != 0) continue;
            }
            catch { continue; }
            yield return full;
        }
    }

    private static bool IsInside(string path, string rootPrefix) =>
        path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) && !path.Contains(@"\..\", StringComparison.Ordinal);

    /// <summary>Deletes empty sub-folders (deepest first). The roots themselves are kept.</summary>
    private static void RemoveEmptyDirectories(Category c, string root, string rootFinal, CancellationToken ct)
    {
        {
            var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var cutoff = DateTime.UtcNow - c.MinAge;
            var dirs = new List<(DirectoryInfo Dir, int Depth)>();
            var stack = new Stack<(DirectoryInfo, int)>();
            stack.Push((new DirectoryInfo(root), 0));
            while (stack.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var (d, depth) = stack.Pop();
                IEnumerable<DirectoryInfo> subs;
                try { subs = d.EnumerateDirectories("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 }).ToList(); }
                catch { continue; }
                foreach (var s in subs)
                {
                    try
                    {
                        if ((s.Attributes & FileAttributes.ReparsePoint) != 0 || !IsInside(s.FullName, rootPrefix)) continue;
                    }
                    catch { continue; }
                    if (depth + 1 >= MaxDepth) continue;
                    dirs.Add((s, depth + 1));
                    stack.Push((s, depth + 1));
                }
            }
            foreach (var (d, _) in dirs.OrderByDescending(x => x.Depth))
            {
                try
                {
                    d.Refresh();
                    if (!d.Exists || (d.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if (c.MinAge > TimeSpan.Zero && d.CreationTimeUtc > cutoff) continue;
                    if (d.EnumerateFileSystemInfos().Any()) continue;
                    // Deleting through a handle fails on a non-empty folder, so nothing inside can be lost.
                    SafeDelete.TryDelete(d.FullName, rootFinal, isDirectory: true, TimeSpan.Zero);
                }
                catch { /* in use or not empty */ }
            }
        }
    }

    // ------------------------------------------------------------------ recycle bin

    private static (long Bytes, long Items) QueryRecycleBin()
    {
        try
        {
            var info = new SHQUERYRBINFO { cbSize = (uint)Marshal.SizeOf<SHQUERYRBINFO>() };
            var hr = SHQueryRecycleBin(null, ref info); // null = all drives
            return hr == 0 ? (info.i64Size, info.i64NumItems) : (0, 0);
        }
        catch (Exception ex)
        {
            Log.Warn("Reciclagem: consulta falhou", ex);
            return (0, 0);
        }
    }

    private static long EmptyRecycleBin()
    {
        var (bytes, items) = QueryRecycleBin();
        if (items == 0) return 0;
        try
        {
            var hr = SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
            if (hr != 0) { Log.Warn($"Reciclagem: SHEmptyRecycleBin devolveu 0x{hr:X8}"); return 0; }
            Log.Info($"Reciclagem esvaziada: {bytes / 1024.0 / 1024:0.0} MB");
            return bytes;
        }
        catch (Exception ex)
        {
            Log.Warn("Reciclagem: falhou", ex);
            return 0;
        }
    }
}
