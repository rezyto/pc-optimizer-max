using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using PcMax.Core.Benchmark;

namespace PcMax.Windows.Benchmark;

/// <summary>
/// Frame-time capture with PresentMon 2.x (optional): only available when PresentMon.exe is shipped
/// next to our executable. Runs:
/// <c>PresentMon.exe --process_id &lt;pid&gt; --output_stdout --timed &lt;s&gt; --terminate_after_timed
/// --stop_existing_session --session_name PcMaxCapture</c> and reads the CSV from stdout.
/// Frame time column: "MsBetweenPresents" (v1 metrics) or "FrameTime" (2.x metrics).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class PresentMonCapture : IFrameCapture
{
    private readonly string _exe = Path.Combine(AppContext.BaseDirectory, "PresentMon.exe");

    public bool IsAvailable => File.Exists(_exe);

    public async Task<IReadOnlyList<double>> CaptureAsync(int processId, TimeSpan duration, CancellationToken ct = default)
    {
        if (!IsAvailable) return Array.Empty<double>();
        if (processId <= 0) throw new ArgumentOutOfRangeException(nameof(processId));
        var seconds = (int)Math.Clamp(Math.Ceiling(duration.TotalSeconds), 1, 600);

        var psi = new ProcessStartInfo(_exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        foreach (var a in new[]
        {
            "--process_id", processId.ToString(CultureInfo.InvariantCulture),
            "--output_stdout",
            "--timed", seconds.ToString(CultureInfo.InvariantCulture),
            "--terminate_after_timed",
            "--stop_existing_session",
            "--session_name", "PcMaxCapture",
        }) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Não foi possível iniciar o PresentMon.");
        var stderr = p.StandardError.ReadToEndAsync(CancellationToken.None);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(seconds + 20));
        var lines = new List<string>();
        try
        {
            while (await p.StandardOutput.ReadLineAsync(timeout.Token).ConfigureAwait(false) is { } line)
                lines.Add(line);
            await p.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch { }
            if (ct.IsCancellationRequested) throw;
            Log.Warn("PresentMon excedeu o tempo; a usar os dados recebidos até agora.");
        }

        var frames = Parse(lines, processId);
        if (frames.Count == 0)
        {
            var err = stderr.IsCompleted ? stderr.Result : "";
            Log.Warn($"PresentMon não devolveu frames (código {(p.HasExited ? p.ExitCode : -1)}): {err.Trim()}");
        }
        return frames;
    }

    /// <summary>Parses PresentMon CSV; keeps the swap chain with most frames (games may have several).</summary>
    internal static IReadOnlyList<double> Parse(IReadOnlyList<string> lines, int processId)
    {
        int headerIdx = lines.ToList().FindIndex(l => l.Contains("MsBetweenPresents", StringComparison.OrdinalIgnoreCase)
                                                   || l.Contains("FrameTime", StringComparison.OrdinalIgnoreCase));
        if (headerIdx < 0) return Array.Empty<double>();
        var header = SplitCsv(lines[headerIdx]);
        int Col(string name) => header.FindIndex(h => h.Trim().Equals(name, StringComparison.OrdinalIgnoreCase));
        var ft = Col("MsBetweenPresents");
        if (ft < 0) ft = Col("FrameTime");
        if (ft < 0) return Array.Empty<double>();
        var pidCol = Col("ProcessID");
        var swapCol = Col("SwapChainAddress");

        var bySwapChain = new Dictionary<string, List<double>>();
        for (var i = headerIdx + 1; i < lines.Count; i++)
        {
            var cols = SplitCsv(lines[i]);
            if (cols.Count <= ft) continue;
            if (pidCol >= 0 && cols.Count > pidCol && int.TryParse(cols[pidCol], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid) && pid != processId)
                continue;
            if (!double.TryParse(cols[ft], NumberStyles.Float, CultureInfo.InvariantCulture, out var ms)) continue; // "NA"
            var key = swapCol >= 0 && cols.Count > swapCol ? cols[swapCol] : "";
            if (!bySwapChain.TryGetValue(key, out var list)) bySwapChain[key] = list = new List<double>();
            list.Add(ms);
        }
        return bySwapChain.Count == 0 ? Array.Empty<double>() : bySwapChain.Values.OrderByDescending(l => l.Count).First();
    }

    private static List<string> SplitCsv(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var quoted = false;
        foreach (var ch in line)
        {
            if (ch == '"') quoted = !quoted;
            else if (ch == ',' && !quoted) { result.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(ch);
        }
        result.Add(sb.ToString());
        return result;
    }
}
