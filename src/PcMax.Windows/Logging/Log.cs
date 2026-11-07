using System.Diagnostics;
using System.Text;

namespace PcMax.Windows;

/// <summary>
/// Tiny thread-safe file logger: <c>pcmax.log</c> in <see cref="Core.Settings.AppPaths.LogsDir"/>,
/// rotated at 1 MB, keeping 5 files (pcmax.log + pcmax.1.log … pcmax.4.log). Never throws.
/// Messages logged before <see cref="Init"/> go to the debugger output only.
/// </summary>
public static class Log
{
    private const long MaxBytes = 1024 * 1024;
    private const int KeepFiles = 5;
    private static readonly object Gate = new();
    private static string? _dir;

    public static void Init(string logsDir)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(logsDir);
                _dir = logsDir;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("PcMax log init failed: " + ex.Message);
            }
        }
    }

    public static void Info(string message) => Write("INFO", message, null);
    public static void Warn(string message, Exception? ex = null) => Write("WARN", message, ex);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        var line = new StringBuilder()
            .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"))
            .Append(" [").Append(level).Append("] ")
            .Append(message);
        if (ex is not null) line.Append(" | ").Append(ex.GetType().Name).Append(": ").Append(ex.Message);
        var text = line.Append(Environment.NewLine).ToString();

        lock (Gate)
        {
            if (_dir is null)
            {
                Debug.Write(text);
                return;
            }
            try
            {
                var path = Path.Combine(_dir, "pcmax.log");
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length + text.Length > MaxBytes) Rotate();
                File.AppendAllText(path, text, Encoding.UTF8);
            }
            catch (Exception e)
            {
                Debug.WriteLine("PcMax log write failed: " + e.Message);
            }
        }
    }

    private static void Rotate()
    {
        string P(int i) => Path.Combine(_dir!, i == 0 ? "pcmax.log" : $"pcmax.{i}.log");
        var oldest = P(KeepFiles - 1);
        if (File.Exists(oldest)) File.Delete(oldest);
        for (var i = KeepFiles - 2; i >= 0; i--)
        {
            if (File.Exists(P(i))) File.Move(P(i), P(i + 1));
        }
    }
}
