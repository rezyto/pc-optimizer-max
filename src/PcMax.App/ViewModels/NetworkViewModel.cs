using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcMax.App.Services;
using PcMax.Core.Network;

namespace PcMax.App.ViewModels;

public sealed record HostOption(string Label, string? Host)
{
    public bool IsCustom => Host is null;
}

public partial class NetworkViewModel : PageViewModel
{
    private readonly AppHost _host;

    public NetworkViewModel(AppHost host)
    {
        _host = host;
        HostOptions = new[]
        {
            new HostOption("Cloudflare · 1.1.1.1", "1.1.1.1"),
            new HostOption("Google · 8.8.8.8", "8.8.8.8"),
            new HostOption("Quad9 · 9.9.9.9", "9.9.9.9"),
            new HostOption("Outro servidor…", null),
        };
        var current = host.Settings.Current.NetworkTestHost;
        _selectedHost = HostOptions.FirstOrDefault(h => h.Host == current) ?? HostOptions[^1];
        _customHost = _selectedHost.IsCustom ? current : "";
    }

    public override string Title => "Rede";
    public override string Subtitle => "Ping, estabilidade e bufferbloat da tua ligação";

    public IReadOnlyList<HostOption> HostOptions { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustom))]
    private HostOption _selectedHost;

    [ObservableProperty] private string _customHost;
    public bool IsCustom => SelectedHost.IsCustom;

    // Ping test
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private string _average = "—";
    [ObservableProperty] private string _jitter = "—";
    [ObservableProperty] private string _loss = "—";
    [ObservableProperty] private string _p95 = "—";
    [ObservableProperty] private string _qualityLabel = "";
    [ObservableProperty] private Tone _qualityTone;
    [ObservableProperty] private string? _advice;
    [ObservableProperty] private IReadOnlyList<double> _samples = Array.Empty<double>();
    [ObservableProperty] private string? _pingError;

    // Bufferbloat
    [ObservableProperty] private bool _hasBloat;
    [ObservableProperty] private string _bloatGrade = "";
    [ObservableProperty] private Tone _bloatTone;
    [ObservableProperty] private string? _bloatDetail;
    [ObservableProperty] private string? _bloatAdvice;
    [ObservableProperty] private string? _bloatProgress;

    partial void OnSelectedHostChanged(HostOption value) => SaveHost();
    partial void OnCustomHostChanged(string value) => SaveHost();

    private string? ResolveHost()
    {
        var h = SelectedHost.Host ?? CustomHost?.Trim();
        if (string.IsNullOrWhiteSpace(h) || h.Length > 253 || h.Any(char.IsWhiteSpace)) return null;
        return h;
    }

    private void SaveHost()
    {
        if (ResolveHost() is { } h) _host.Settings.Update(s => s with { NetworkTestHost = h });
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task TestAsync(CancellationToken ct)
    {
        PingError = null;
        if (ResolveHost() is not { } host) { PingError = "Escreve um endereço válido (ex.: 1.1.1.1 ou servidor.exemplo.pt)."; return; }
        try
        {
            var (stats, samples) = await Task.Run(() => _host.Network.PingTestAsync(host, 30, ct), ct);
            Show(stats);
            ShowSamples(samples);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AppLog.Warn("Teste de ping falhou", ex);
            PingError = "Não foi possível testar a ligação.";
        }
    }

    /// <summary>Displays a result (also used directly by tests and screenshots).</summary>
    public void Show(NetworkStats s)
    {
        Average = Format.Ms(s.AverageMs);
        Jitter = Format.Ms(s.JitterMs);
        Loss = s.LossPercent.ToString("0.#", Format.Pt) + "%";
        P95 = Format.Ms(s.P95Ms);
        QualityLabel = NetworkTester.QualityLabel(s.Quality);
        QualityTone = ToneOf(s.Quality);
        Advice = NetworkTester.Advice(s);
        HasResult = true;
    }

    public void ShowSamples(IReadOnlyList<double?> rtts) => Samples = rtts.Where(x => x.HasValue).Select(x => x!.Value).ToArray();

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task BufferbloatAsync(CancellationToken ct)
    {
        PingError = null;
        if (ResolveHost() is not { } host) { PingError = "Escreve um endereço válido."; return; }
        var progress = new Progress<string>(m => BloatProgress = m);
        try
        {
            var r = await Task.Run(() => _host.Network.BufferbloatAsync(host, progress, ct), ct);
            ShowBloat(r);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AppLog.Warn("Teste de bufferbloat falhou", ex);
            PingError = "Não foi possível fazer o teste de bufferbloat.";
        }
        finally { BloatProgress = null; }
    }

    public void ShowBloat(BufferbloatResult r)
    {
        BloatGrade = r.Grade;
        BloatTone = r.Grade switch { "A+" or "A" => Tone.Good, "B" => Tone.Info, "C" => Tone.Warning, "?" => Tone.Neutral, _ => Tone.Critical };
        BloatDetail = $"Parado: {Format.Ms(r.Idle.AverageMs)} · A transferir: {Format.Ms(r.Loaded.AverageMs)} · Subida: +{Format.Ms(r.IncreaseMs)}";
        BloatAdvice = r.Advice;
        HasBloat = true;
    }

    public static Tone ToneOf(ConnectionQuality q) => q switch
    {
        ConnectionQuality.Excellent => Tone.Good,
        ConnectionQuality.Good => Tone.Info,
        ConnectionQuality.Fair => Tone.Warning,
        _ => Tone.Critical,
    };
}
