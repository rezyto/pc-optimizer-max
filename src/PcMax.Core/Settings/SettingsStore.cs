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
            var s = JsonSerializer.Deserialize<AppSettings>(AtomicFile.ReadAllTextCapped(_path), JsonDefaults.Options);
            return (s ?? new AppSettings()).Normalize();
        }
        catch (Exception)
        {
            // Anything wrong with the file (corrupt, too big, nulls where objects belong, no access):
            // keep a copy for diagnosis and start with defaults. Never let a bad file stop the app.
            AtomicFile.Backup(_path);
            return new AppSettings().Normalize();
        }
    }

    public void Save(AppSettings settings) =>
        AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(settings.Normalize(), JsonDefaults.Options));
}
