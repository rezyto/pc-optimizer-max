namespace PcMax.Core.Network;

/// <summary>Result of a series of pings. Null round-trip = lost packet.</summary>
public sealed record NetworkStats(
    string Host,
    int Sent,
    int Received,
    double? AverageMs,
    double? MinMs,
    double? MaxMs,
    double? P95Ms,
    double? JitterMs)
{
    public double LossPercent => Sent == 0 ? 0 : (Sent - Received) * 100.0 / Sent;

    public static NetworkStats FromSamples(string host, IReadOnlyList<double?> rttMs)
    {
        var ok = rttMs.Where(x => x is >= 0).Select(x => x!.Value).ToList();
        if (ok.Count == 0) return new NetworkStats(host, rttMs.Count, 0, null, null, null, null, null);
        var sorted = ok.OrderBy(x => x).ToList();
        var p95 = sorted[Math.Clamp((int)Math.Ceiling(sorted.Count * 0.95) - 1, 0, sorted.Count - 1)];
        // Jitter: mean absolute difference between consecutive successful samples (RFC 3550 style, simplified).
        double? jitter = null;
        if (ok.Count >= 2)
            jitter = ok.Zip(ok.Skip(1), (a, b) => Math.Abs(b - a)).Average();
        return new NetworkStats(host, rttMs.Count, ok.Count, ok.Average(), sorted[0], sorted[^1], p95, jitter);
    }

    public ConnectionQuality Quality
    {
        get
        {
            if (Received == 0) return ConnectionQuality.Offline;
            var loss = LossPercent;
            if (loss >= 5 || AverageMs > 150 || JitterMs > 30) return ConnectionQuality.Bad;
            if (loss >= 1 || AverageMs > 80 || JitterMs > 15) return ConnectionQuality.Fair;
            if (AverageMs > 40 || JitterMs > 5) return ConnectionQuality.Good;
            return ConnectionQuality.Excellent;
        }
    }
}

public enum ConnectionQuality { Offline, Bad, Fair, Good, Excellent }

/// <summary>
/// Bufferbloat: how much the ping rises when the connection is busy (download/upload).
/// Graded like common public bufferbloat tests.
/// </summary>
public static class Bufferbloat
{
    public static (string Grade, double IncreaseMs) Grade(NetworkStats idle, NetworkStats loaded)
    {
        if (idle.AverageMs is not { } i || loaded.AverageMs is not { } l) return ("?", 0);
        var inc = Math.Max(0, l - i);
        var grade = inc switch
        {
            < 5 => "A+",
            < 30 => "A",
            < 60 => "B",
            < 200 => "C",
            < 400 => "D",
            _ => "F",
        };
        return (grade, inc);
    }

    public static string Advice(string grade) => grade switch
    {
        "A+" or "A" => "Ótimo: a tua ligação mantém o ping baixo mesmo quando está ocupada.",
        "B" => "Razoável: downloads de outras pessoas em casa podem causar picos de lag.",
        "?" => "Não foi possível medir.",
        _ => "Mau: quando alguém usa a internet (downloads, streaming), o teu ping dispara. Ativa SQM/QoS no router (procura 'Smart Queue', 'QoS' ou 'Cake') e limita a velocidade a ~90% da contratada.",
    };
}

public interface INetworkProbe
{
    /// <summary>Sends <paramref name="count"/> pings and returns the round-trip times (null = lost).</summary>
    Task<IReadOnlyList<double?>> PingAsync(string host, int count, TimeSpan interval, CancellationToken ct = default);
}
