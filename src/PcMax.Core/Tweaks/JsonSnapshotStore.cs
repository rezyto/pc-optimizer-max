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
        if (!File.Exists(_path)) return Array.Empty<TweakSnapshot>();
        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<List<TweakSnapshot>>(json, JsonDefaults.Options) ?? new();
        }
        catch (JsonException)
        {
            // Corrupt file: keep a copy for diagnosis, start clean.
            AtomicFile.Backup(_path);
            return Array.Empty<TweakSnapshot>();
        }
    }

    public void SaveActive(IReadOnlyList<TweakSnapshot> snapshots) =>
        AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(snapshots, JsonDefaults.Options));
}
