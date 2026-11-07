using PcMax.App.Services.Fakes;
using PcMax.Core.Network;

namespace PcMax.App.Services;

/// <summary>Creates network load for the bufferbloat test.</summary>
public interface ILoadGenerator
{
    /// <summary>Downloads (and discards) data until cancelled or <paramref name="max"/> elapses.</summary>
    Task RunAsync(TimeSpan max, CancellationToken ct);
}

public sealed class HttpDownloadLoad : ILoadGenerator
{
    public const string Url = "https://speed.cloudflare.com/__down?bytes=25000000";
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    public async Task RunAsync(TimeSpan max, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(max);
        var buffer = new byte[81920];
        try
        {
            // Two parallel streams to really fill the line.
            await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
            {
                using var resp = await Http.GetAsync(Url, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
                await using var s = await resp.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
                var local = new byte[buffer.Length];
                while (await s.ReadAsync(local, cts.Token).ConfigureAwait(false) > 0) { /* discard */ }
            })).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { /* time is up */ }
    }
}

public sealed class FakeLoadGenerator : ILoadGenerator
{
    public async Task RunAsync(TimeSpan max, CancellationToken ct)
    {
        FakeLoad.Busy = true;
        try { await Task.Delay(max, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        finally { FakeLoad.Busy = false; }
    }
}

public sealed record BufferbloatResult(NetworkStats Idle, NetworkStats Loaded, string Grade, double IncreaseMs, string Advice);

public sealed class NetworkTester
{
    private readonly INetworkProbe _probe;
    private readonly ILoadGenerator _load;

    public NetworkTester(INetworkProbe probe, ILoadGenerator load)
    {
        _probe = probe;
        _load = load;
    }

    public async Task<(NetworkStats Stats, IReadOnlyList<double?> Samples)> PingTestAsync(string host, int count, CancellationToken ct)
    {
        var rtts = await _probe.PingAsync(host, count, TimeSpan.FromMilliseconds(250), ct).ConfigureAwait(false);
        return (NetworkStats.FromSamples(host, rtts), rtts);
    }

    public async Task<BufferbloatResult> BufferbloatAsync(string host, IProgress<string>? progress, CancellationToken ct)
    {
        progress?.Report("A medir o ping com a ligação parada…");
        var idle = NetworkStats.FromSamples(host,
            await _probe.PingAsync(host, 15, TimeSpan.FromMilliseconds(200), ct).ConfigureAwait(false));

        progress?.Report("A medir o ping durante uma transferência…");
        using var loadCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var load = _load.RunAsync(TimeSpan.FromSeconds(10), loadCts.Token);
        IReadOnlyList<double?> loadedRtts;
        try
        {
            await Task.Delay(1200, ct).ConfigureAwait(false); // let the download ramp up
            loadedRtts = await _probe.PingAsync(host, 15, TimeSpan.FromMilliseconds(300), ct).ConfigureAwait(false);
        }
        finally
        {
            loadCts.Cancel();
            try { await load.ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                AppLog.Warn("Transferência do teste de bufferbloat falhou", ex);
            }
        }
        var loaded = NetworkStats.FromSamples(host, loadedRtts);
        var (grade, inc) = Bufferbloat.Grade(idle, loaded);
        return new BufferbloatResult(idle, loaded, grade, inc, Bufferbloat.Advice(grade));
    }

    /// <summary>Plain-language explanation of a ping test.</summary>
    public static string Advice(NetworkStats s)
    {
        if (s.Quality == ConnectionQuality.Offline)
            return "Sem resposta. Confirma se estás ligado à internet ou experimenta outro servidor.";
        var parts = new List<string>();
        if (s.LossPercent >= 1)
            parts.Add($"Perdeste {s.LossPercent.ToString("0.#", Format.Pt)}% dos pacotes: isto causa teletransportes e tiros que não registam. Costuma vir do Wi-Fi ou de cabos/router com problemas.");
        if (s.JitterMs > 10)
            parts.Add("O ping varia muito (jitter alto): sentes isto como lag irregular. Liga por cabo e evita transferências durante o jogo.");
        if (s.AverageMs > 80)
            parts.Add("O ping médio está alto: escolhe servidores de jogo mais perto (ex.: EU West) ou verifica o teu operador.");
        if (parts.Count == 0)
            parts.Add(s.Quality == ConnectionQuality.Excellent
                ? "Ligação excelente para jogar online."
                : "Ligação boa para jogar. Por cabo costuma ficar ainda mais estável.");
        return string.Join(" ", parts);
    }

    public static string QualityLabel(ConnectionQuality q) => q switch
    {
        ConnectionQuality.Excellent => "Excelente",
        ConnectionQuality.Good => "Boa",
        ConnectionQuality.Fair => "Razoável",
        ConnectionQuality.Bad => "Má",
        _ => "Sem ligação",
    };
}
