namespace PcMax.Core.Benchmark;

/// <summary>Statistics computed from frame times (milliseconds per frame).</summary>
public sealed record FrameStats(int Frames, double AverageFps, double OnePercentLowFps, double PointOnePercentLowFps, double AverageFrameTimeMs, double FrameTimeStdDevMs)
{
    /// <summary>
    /// 1% low = average FPS of the slowest 1% of frames (what you feel as stutter).
    /// Requires at least 100 frames; invalid or absurd frame times (≤0 or &gt;1000 ms) are ignored.
    /// </summary>
    public static FrameStats? FromFrameTimes(IEnumerable<double> frameTimesMs)
    {
        var ft = frameTimesMs.Where(x => x > 0 && x <= 1000 && !double.IsNaN(x)).ToArray();
        if (ft.Length < 100) return null;
        var total = ft.Sum();
        var avgFt = total / ft.Length;
        var avgFps = 1000.0 * ft.Length / total;
        var sorted = ft.OrderByDescending(x => x).ToArray(); // slowest first
        double LowFps(double fraction)
        {
            var n = Math.Max(1, (int)Math.Ceiling(sorted.Length * fraction));
            return 1000.0 / sorted.Take(n).Average();
        }
        var variance = ft.Select(x => (x - avgFt) * (x - avgFt)).Sum() / ft.Length;
        return new FrameStats(ft.Length, avgFps, LowFps(0.01), LowFps(0.001), avgFt, Math.Sqrt(variance));
    }
}

public enum BenchmarkVerdict { Better, NoChange, Worse }

public sealed record BenchmarkComparison(FrameStats Before, FrameStats After, double AvgFpsChangePercent, double OnePercentLowChangePercent, BenchmarkVerdict Verdict, string Summary)
{
    /// <summary>
    /// Changes smaller than the noise margin are treated as "no change" so placebo tweaks are not kept.
    /// The 1% lows weigh more than the average because they are what the player feels.
    /// </summary>
    public static BenchmarkComparison Compare(FrameStats before, FrameStats after, double noisePercent = 3.0)
    {
        static double Pct(double a, double b) => a <= 0 ? 0 : (b - a) / a * 100.0;
        var avg = Pct(before.AverageFps, after.AverageFps);
        var low = Pct(before.OnePercentLowFps, after.OnePercentLowFps);
        var score = 0.4 * avg + 0.6 * low;
        var verdict = score > noisePercent ? BenchmarkVerdict.Better
            : score < -noisePercent ? BenchmarkVerdict.Worse
            : BenchmarkVerdict.NoChange;
        var summary = verdict switch
        {
            BenchmarkVerdict.Better => $"Melhorou: FPS médio {avg:+0.0;-0.0}%, 1% lows {low:+0.0;-0.0}%. O ajuste fica ativo.",
            BenchmarkVerdict.Worse => $"Piorou: FPS médio {avg:+0.0;-0.0}%, 1% lows {low:+0.0;-0.0}%. O ajuste foi revertido.",
            _ => $"Sem diferença real (FPS médio {avg:+0.0;-0.0}%, 1% lows {low:+0.0;-0.0}%, dentro da margem de ruído de ±{noisePercent:0}%). O ajuste foi revertido para não mexer no PC sem motivo.",
        };
        return new BenchmarkComparison(before, after, avg, low, verdict, summary);
    }
}

/// <summary>Captures frame times of a running game (implemented on Windows with PresentMon).</summary>
public interface IFrameCapture
{
    bool IsAvailable { get; }
    Task<IReadOnlyList<double>> CaptureAsync(int processId, TimeSpan duration, CancellationToken ct = default);
}
