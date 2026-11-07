using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcMax.App.Services;
using PcMax.Core.Doctor;
using PcMax.Core.Sensors;

namespace PcMax.App.ViewModels;

public sealed record ResultLine(string Title, string? Detail, Tone Tone);

public partial class DashboardViewModel : PageViewModel
{
    private readonly AppHost _host;
    private readonly Action<PageViewModel>? _navigate;
    private Func<PageViewModel>? _doctorPage;
    private bool _initialAnalysisStarted;

    public DashboardViewModel(AppHost host, Action<PageViewModel>? navigate = null)
    {
        _host = host;
        _navigate = navigate;
        var t = host.Settings.Current.Temperatures;
        CpuTemp = new MetricViewModel("CPU", "Icon.Cpu", 100, MetricViewModel.Thresholds(t.CpuWarning, t.CpuCritical), v => Format.Temp(v));
        GpuTemp = new MetricViewModel("Gráfica", "Icon.Gpu", 100, MetricViewModel.Thresholds(t.GpuWarning, t.GpuCritical), v => Format.Temp(v));
        CpuLoad = new MetricViewModel("Uso do CPU", "Icon.Cpu", 100, MetricViewModel.Thresholds(85, 97), v => Format.Percent(v));
        GpuLoad = new MetricViewModel("Uso da gráfica", "Icon.Gpu", 100, MetricViewModel.Thresholds(101, 101), v => Format.Percent(v));
        Ram = new MetricViewModel("Memória RAM", "Icon.Ram", 100, MetricViewModel.Thresholds(80, 92), v => Format.Percent(v));

        host.SensorsUpdated += (_, s) => ApplySensors(s);
        host.GameStatusChanged += (_, _) => UpdateGameStatus();
        host.Health.Analyzed += (_, _) => host.Ui.Post(UpdateHealth);
        host.Settings.Changed += (_, c) =>
        {
            if (c.Old.Temperatures == c.New.Temperatures) return;
            CpuTemp.Classify = MetricViewModel.Thresholds(c.New.Temperatures.CpuWarning, c.New.Temperatures.CpuCritical);
            GpuTemp.Classify = MetricViewModel.Thresholds(c.New.Temperatures.GpuWarning, c.New.Temperatures.GpuCritical);
        };
        UpdateGameStatus();
        UpdateHealth();
    }

    public override string Title => "Painel";
    public override string Subtitle => "O estado do teu PC em tempo real";

    public MetricViewModel CpuTemp { get; }
    public MetricViewModel GpuTemp { get; }
    public MetricViewModel CpuLoad { get; }
    public MetricViewModel GpuLoad { get; }
    public MetricViewModel Ram { get; }

    [ObservableProperty] private int _healthScore;
    [ObservableProperty] private bool _hasHealth;
    [ObservableProperty] private string _healthLabel = "A analisar…";
    [ObservableProperty] private string _healthHint = "A primeira análise demora alguns segundos.";
    [ObservableProperty] private Tone _healthTone = Tone.Neutral;
    [ObservableProperty] private string? _pcSummary;

    [ObservableProperty] private string _gameStatus = "";
    [ObservableProperty] private bool _isGameActive;

    [ObservableProperty] private string? _progressText;
    [ObservableProperty] private string? _resultSummary;
    [ObservableProperty] private string? _warning;
    public ObservableCollection<ResultLine> Results { get; } = new();

    public bool CpuSensorsNeedDriver => _host.Platform.Sensors.CpuSensorsNeedDriver;

    public void SetDoctorPage(Func<PageViewModel> doctor) => _doctorPage = doctor;

    public override void OnActivated()
    {
        if (_initialAnalysisStarted || _host.Health.LastFindings is not null) return;
        _initialAnalysisStarted = true;
        _ = Task.Run(async () =>
        {
            try { await _host.Health.AnalyzeAsync(); }
            catch (Exception ex) { AppLog.Warn("Análise inicial falhou", ex); }
        });
    }

    public void ApplySensors(SensorSnapshot s)
    {
        CpuTemp.Push(s.CpuTemperatureC, s.CpuClockMhz is { } c ? $"{c / 1000:0.0} GHz" : null);
        GpuTemp.Push(s.GpuTemperatureC, s.GpuClockMhz is { } g ? $"{g:0} MHz" : null);
        CpuLoad.Push(s.CpuLoadPercent, s.CpuPowerW is { } p ? $"{p:0} W" : null);
        GpuLoad.Push(s.GpuLoadPercent, s.GpuPowerW is { } gp ? $"{gp:0} W" : null);
        Ram.Push(s.RamUsedPercent, s.RamUsedGb is { } u && s.RamTotalGb is { } tot
            ? $"{u.ToString("0.0", Format.Pt)} / {tot:0} GB" : null);
    }

    private void UpdateGameStatus()
    {
        GameStatus = _host.GameStatusText;
        IsGameActive = _host.IsGameActive;
    }

    private void UpdateHealth()
    {
        if (_host.Health.Score is not { } score) return;
        HasHealth = true;
        HealthScore = score;
        (HealthLabel, HealthTone) = score switch
        {
            >= 85 => ("Excelente", Tone.Good),
            >= 65 => ("Bom, mas pode melhorar", Tone.Info),
            >= 40 => ("Precisa de atenção", Tone.Warning),
            _ => ("Muito a melhorar", Tone.Critical),
        };
        var findings = _host.Health.LastFindings!;
        var auto = _host.Optimizer.AutoFixable(findings).Count;
        var manual = findings.Count(f => f.Fix is { IsAutomatic: false });
        HealthHint = findings.Count == 0
            ? "Não encontrámos problemas."
            : $"{Format.Count(findings.Count, "ponto encontrado", "pontos encontrados")} · {auto} com correção automática · {Format.Count(manual, "precisa", "precisam")} de ti";
        if (_host.Health.LastProfile is { } p)
            PcSummary = string.Join(" · ", new[] { p.CpuName, p.Gpus.FirstOrDefault()?.Name, p.TotalRamGb is { } r ? $"{r:0} GB RAM" : null }
                .Where(x => !string.IsNullOrEmpty(x)));
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task OptimizeAsync(CancellationToken ct)
    {
        Results.Clear();
        ResultSummary = null;
        Warning = null;
        var progress = new Progress<string>(m => ProgressText = m);
        try
        {
            var report = await Task.Run(() => _host.Optimizer.OptimizeAsync(progress, ct), ct);
            foreach (var s in report.Steps)
                Results.Add(new ResultLine(s.Title, s.Detail, s.Ok ? Tone.Good : Tone.Warning));
            var applied = report.Steps.Count(s => s.Ok);
            ResultSummary = report.Steps.Count == 0
                ? "Não há correções automáticas a fazer. "
                : $"Pronto! Pontuação {report.ScoreBefore} → {report.ScoreAfter}. ";
            if (report.ManualFindings > 0)
                ResultSummary += $"{Format.Count(report.ManualFindings, "melhoria precisa", "melhorias precisam")} de ti: vê o Médico do PC.";
            Warning = report.Warning;
        }
        catch (OperationCanceledException)
        {
            ResultSummary = "Cancelado. O que já foi aplicado pode ser revertido no Histórico.";
        }
        catch (Exception ex)
        {
            AppLog.Error("Otimizar agora falhou", ex);
            ResultSummary = "Algo correu mal. Nada ficou a meio: vê o Histórico para confirmar.";
        }
        finally
        {
            ProgressText = null;
        }
    }

    [RelayCommand]
    private void OpenDoctor()
    {
        if (_doctorPage is not null) _navigate?.Invoke(_doctorPage());
    }
}
