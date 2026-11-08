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
            return Parse(AtomicFile.ReadAllTextCapped(_path));
        }
        catch (Exception)
        {
            // Anything wrong with the file (corrupt, too big, nulls where objects belong, no access):
            // keep a copy for diagnosis and start with defaults. Never let a bad file stop the app.
            AtomicFile.Backup(_path);
            return new AppSettings().Normalize();
        }
    }

    /// <summary>
    /// Parses and normalizes settings JSON with no side effects (no backup file). Throws <see cref="JsonException"/>
    /// on malformed JSON; every field that is null or out of range becomes its default.
    /// </summary>
    public static AppSettings Parse(string json) =>
        (JsonSerializer.Deserialize<AppSettings>(json, JsonDefaults.Options) ?? new AppSettings()).Normalize();

    public void Save(AppSettings settings) =>
        AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(settings.Normalize(), JsonDefaults.Options));
}
