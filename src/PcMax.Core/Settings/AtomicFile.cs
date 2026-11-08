namespace PcMax.Core.Settings;

/// <summary>Writes files so that a crash mid-write never leaves a half-written file.</summary>
public static class AtomicFile
{
    public static void WriteAllText(string path, string contents)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        if (File.Exists(tmp)) File.Delete(tmp); // a leftover (or planted link) is removed, never written through
        File.WriteAllText(tmp, contents);
        if (File.Exists(path))
            File.Replace(tmp, path, null);
        else
            File.Move(tmp, path);
    }

    /// <summary>State files are tiny; anything bigger is corrupt or hostile and is not read.</summary>
    public const long MaxStateFileBytes = 1024 * 1024;

    /// <summary>Reads a state file, refusing files over <see cref="MaxStateFileBytes"/> and links (reparse points).</summary>
    public static string ReadAllTextCapped(string path, long maxBytes = MaxStateFileBytes)
    {
        var fi = new FileInfo(path);
        if ((fi.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException($"{path} é um atalho/link; ignorado.");
        if (fi.Length > maxBytes) throw new IOException($"{path} é demasiado grande ({fi.Length} bytes).");
        return File.ReadAllText(path);
    }

    public static void Backup(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Copy(path, path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"), overwrite: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
