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
            var json = AtomicFile.ReadAllTextCapped(_path);
            var list = JsonSerializer.Deserialize<List<TweakSnapshot>>(json, JsonDefaults.Options) ?? new();
            return list.Where(s => s is not null && s.PreviousValues is not null).Take(100).ToList();
        }
        catch (Exception)
        {
            // Corrupt file: keep a copy for diagnosis, start clean.
            AtomicFile.Backup(_path);
            return Array.Empty<TweakSnapshot>();
        }
    }

    public void SaveActive(IReadOnlyList<TweakSnapshot> snapshots) =>
        AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(snapshots, JsonDefaults.Options));
}
