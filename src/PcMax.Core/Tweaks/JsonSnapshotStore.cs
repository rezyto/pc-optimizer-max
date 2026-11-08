using System.Text.Json;
using PcMax.Core.Settings;

namespace PcMax.Core.Tweaks;

/// <summary>Stores active snapshots as JSON, written atomically (temp file + replace).</summary>
public sealed class JsonSnapshotStore : ISnapshotStore
{
    private readonly string _path;
    public JsonSnapshotStore(string path) => _path = path;

    public IReadOnlyList<TweakSnapshot> LoadActive()
    {
        try
        {
            if (!File.Exists(_path)) return Array.Empty<TweakSnapshot>();
            return Parse(AtomicFile.WithRetry(() => AtomicFile.ReadAllTextCapped(_path)));
        }
        catch (Exception)
        {
            // Corrupt file: keep a copy for diagnosis, start clean.
            AtomicFile.Backup(_path);
            return Array.Empty<TweakSnapshot>();
        }
    }

    /// <summary>Parses a snapshot file with no side effects. Throws <see cref="JsonException"/> on malformed JSON.</summary>
    public static IReadOnlyList<TweakSnapshot> Parse(string json)
    {
        var list = JsonSerializer.Deserialize<List<TweakSnapshot?>>(json, JsonDefaults.Options) ?? new();
        return list
            .Where(s => s is not null && !string.IsNullOrEmpty(s.TweakId) && s.PreviousValues is not null)
            .Select(s => s!)
            .Take(100)
            .ToList();
    }

    public void SaveActive(IReadOnlyList<TweakSnapshot> snapshots) =>
        AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(snapshots, JsonDefaults.Options));
}
