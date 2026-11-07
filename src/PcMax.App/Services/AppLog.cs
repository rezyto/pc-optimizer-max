using System.Text;
using PcMax.Core.Settings;

namespace PcMax.App.Services;

/// <summary>
/// App-wide log. On Windows it forwards to the platform log (PcMax.Windows.Log) through
/// <see cref="PlatformFactory"/>; elsewhere it writes its own daily file under <see cref="AppPaths.LogsDir"/>.
/// Never throws.
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static string? _dir;

    public static void Init(AppPaths paths)
    {
        _dir = paths.LogsDir;
        try { Directory.CreateDirectory(_dir); } catch { /* logging must never break the app */ }
        try { PlatformFactory.InitLog(paths); } catch { }
    }

    public static void Info(string message) => Write("INFO", message, null);
    public static void Warn(string message, Exception? ex = null) => Write("WARN", message, ex);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            if (PlatformFactory.ForwardLog(level, message, ex)) return;
        }
        catch { /* fall back to our own file */ }

        var text = ex is null ? message : message + " | " + ex;
        if (_dir is null) return;
        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {text}{Environment.NewLine}";
            lock (Gate)
                File.AppendAllText(Path.Combine(_dir, $"app-{DateTime.Now:yyyyMMdd}.log"), line, Encoding.UTF8);
        }
        catch { }
    }
}
