using PcMax.Core.Settings;

namespace PcMax.App.Services;

/// <summary>Marker file under <see cref="AppPaths.Root"/> so the welcome dialog shows only once.</summary>
public static class FirstRun
{
    private static string MarkerPath(AppPaths p) => Path.Combine(p.Root, "welcome-shown");

    public static bool IsFirstRun(AppPaths p) => !File.Exists(MarkerPath(p));

    public static void MarkDone(AppPaths p)
    {
        try
        {
            Directory.CreateDirectory(p.Root);
            File.WriteAllText(MarkerPath(p), DateTimeOffset.Now.ToString("O"));
        }
        catch (Exception ex) { AppLog.Warn("Não foi possível gravar o marcador de primeira execução", ex); }
    }
}
