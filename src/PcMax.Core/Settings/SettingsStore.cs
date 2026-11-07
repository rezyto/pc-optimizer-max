using System.Text.Json;

namespace PcMax.Core.Settings;

public sealed class SettingsStore
{
    private readonly string _path;
    public SettingsStore(string path) => _path = path;

    public string Path => _path;

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new AppSettings().Normalize();
            var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), JsonDefaults.Options);
            return (s ?? new AppSettings()).Normalize();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            AtomicFile.Backup(_path);
            return new AppSettings().Normalize();
        }
    }

    public void Save(AppSettings settings) =>
        AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(settings.Normalize(), JsonDefaults.Options));
}
