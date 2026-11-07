using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcMax.App.Services;
using PcMax.Core.Doctor;
using PcMax.Core.Tweaks;

namespace PcMax.App.ViewModels;

public sealed record GuideStep(int Number, string Text);

public partial class FindingViewModel : ViewModelBase
{
    private readonly AppHost _host;

    public FindingViewModel(Finding f, AppHost host)
    {
        _host = host;
        Finding = f;
        var tweak = f.Fix?.TweakId is { } id ? host.Engine.Find(id) : null;
        Tweak = tweak;
        AutoInGames = tweak is { GameSessionOnly: true };
        CanAutoFix = tweak is not null && !AutoInGames;
        IsFixed = tweak is not null && !AutoInGames && host.Engine.IsActive(tweak.Id);
        Steps = (f.Fix?.GuideSteps ?? Array.Empty<string>()).Select((s, i) => new GuideStep(i + 1, s)).ToList();
        RiskTooHigh = tweak is not null && tweak.Risk > host.Settings.Current.MaxRisk;
    }

    public Finding Finding { get; }
    public ITweak? Tweak { get; }
    public string Title => Finding.Title;
    public string Explanation => Finding.Explanation;
    public string? Impact => Finding.EstimatedImpact;
    public bool HasImpact => !string.IsNullOrEmpty(Finding.EstimatedImpact);
    public bool CanAutoFix { get; }
    public bool AutoInGames { get; }
    public bool RiskTooHigh { get; }
    public IReadOnlyList<GuideStep> Steps { get; }
    public bool HasSteps => Steps.Count > 0;

    public Tone Tone => Finding.Severity switch
    {
        FindingSeverity.Critical => Tone.Critical,
        FindingSeverity.Problem => Tone.Warning,
        FindingSeverity.Tip => Tone.Info,
        _ => Tone.Good,
    };

    public string SeverityLabel => Finding.Severity switch
    {
        FindingSeverity.Critical => "Grande impacto",
        FindingSeverity.Problem => "Problema",
        FindingSeverity.Tip => "Dica",
        _ => "Tudo bem",
    };

    public string SeverityIcon => Finding.Severity switch
    {
        FindingSeverity.Critical or FindingSeverity.Problem => "Icon.Warning",
        FindingSeverity.Tip => "Icon.Info",
        _ => "Icon.Check",
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFixButton))]
    private bool _isFixed;

    [ObservableProperty] private string? _fixResult;

    public bool ShowFixButton => CanAutoFix && !IsFixed;

    [RelayCommand]
    private async Task FixAsync(CancellationToken ct)
    {
        if (Tweak is null) return;
        FixResult = null;
        try
        {
            var warning = await Task.Run(() => _host.Guard.EnsureRestorePointAsync(ct), ct);
            var entry = await Task.Run(() => _host.Engine.ApplyAsync(Tweak.Id, ct), ct);
            if (entry.Action == HistoryAction.Applied)
            {
                IsFixed = true;
                FixResult = warning ?? "Corrigido. Podes reverter no Histórico quando quiseres.";
            }
            else
            {
                FixResult = OptimizerService.Describe(entry);
            }
        }
        catch (OperationCanceledException) { FixResult = "Cancelado."; }
        catch (Exception ex)
        {
            AppLog.Error($"Correção {Tweak.Id} falhou", ex);
            FixResult = "Não foi possível corrigir. Nada foi alterado.";
        }
    }
}

public partial class DoctorViewModel : PageViewModel
{
    private readonly AppHost _host;
    private readonly IDialogService? _dialogs;

    public DoctorViewModel(AppHost host, IDialogService? dialogs)
    {
        _host = host;
        _dialogs = dialogs;
        host.Health.Analyzed += (_, _) => host.Ui.Post(Load);
        Load();
    }

    public override string Title => "Médico do PC";
    public override string Subtitle => "Encontra o que está mesmo a travar o teu PC";

    public ObservableCollection<FindingViewModel> Findings { get; } = new();
    [ObservableProperty] private bool _hasResults;
    [ObservableProperty] private bool _noProblems;
    [ObservableProperty] private string? _summary;
    [ObservableProperty] private string? _error;

    public bool ShowPawnIoNotice => _host.Platform.Sensors.CpuSensorsNeedDriver;
    public const string PawnIoUrl = "https://pawnio.eu";

    private void Load()
    {
        if (_host.Health.LastFindings is not { } list) return;
        Findings.Clear();
        foreach (var f in list.OrderByDescending(x => x.ImpactScore).ThenByDescending(x => x.Severity))
            Findings.Add(new FindingViewModel(f, _host));
        HasResults = true;
        NoProblems = list.Count == 0;
        var auto = Findings.Count(f => f.ShowFixButton);
        Summary = list.Count == 0
            ? "Está tudo em ordem. Bom jogo!"
            : $"Pontuação {_host.Health.Score}/100 · {Format.Count(list.Count, "ponto", "pontos")}, do maior para o menor impacto · {auto} com correção automática";
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task AnalyzeAsync(CancellationToken ct)
    {
        Error = null;
        try
        {
            await Task.Run(() => _host.Health.AnalyzeAsync(ct), ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AppLog.Error("Análise falhou", ex);
            Error = "Não foi possível analisar o PC. Tenta de novo.";
        }
    }

    [RelayCommand]
    private void OpenPawnIo() => _dialogs?.OpenUrl(PawnIoUrl);
}
