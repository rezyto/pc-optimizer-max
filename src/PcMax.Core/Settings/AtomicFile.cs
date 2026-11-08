namespace PcMax.Core.Settings;

/// <summary>Writes files so that a crash mid-write never leaves a half-written file.</summary>
public static class AtomicFile
{
    /// <summary>
    /// Writes to a fresh temp file (CreateNew: never through an existing file or planted link), flushes it to the
    /// disk itself (so a power cut right after the rename cannot leave an empty or garbage file), then swaps it in.
    /// A unique temp name per call keeps two writers of the same file from colliding.
    /// </summary>
    public static void WriteAllText(string path, string contents)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);
        var tmp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            using (var w = new StreamWriter(fs, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                w.Write(contents);
                w.Flush();
                fs.Flush(flushToDisk: true);
            }
            if (File.Exists(path))
                File.Replace(tmp, path, null);
            else
                File.Move(tmp, path);
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            throw;
        }
        // Leftover from older versions (fixed ".tmp" name).
        try { if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp"); } catch { }
    }

    /// <summary>Runs a read, retrying briefly on sharing violations (antivirus, backup tools hold files for a moment).</summary>
    public static T WithRetry<T>(Func<T> read, int attempts = 4, int delayMs = 150)
    {
        for (var i = 1; ; i++)
        {
            try { return read(); }
            catch (Exception ex) when (i < attempts && ex is IOException or UnauthorizedAccessException && ex is not FileNotFoundException and not DirectoryNotFoundException)
            {
                Thread.Sleep(delayMs);
            }
        }
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
