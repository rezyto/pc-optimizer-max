using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcMax.App.Services;
using PcMax.Core.Tweaks;

namespace PcMax.App.ViewModels;

public sealed record HistoryItem(string When, string Name, string ActionLabel, string? Detail, Tone Tone);

public partial class ActiveTweakViewModel : ViewModelBase
{
    private readonly HistoryViewModel _owner;

    public ActiveTweakViewModel(HistoryViewModel owner, TweakSnapshot snap, ITweak? tweak)
    {
        _owner = owner;
        Id = snap.TweakId;
        Name = tweak?.Name ?? snap.TweakId;
        Description = tweak?.Description ?? "Ajuste de uma versão anterior.";
        Since = "Desde " + snap.AppliedAt.ToLocalTime().ToString("dd/MM HH:mm", Format.Pt);
        Scope = tweak?.GameSessionOnly == true ? "Só durante o jogo" : "Permanente até reverteres";
    }

    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public string Since { get; }
    public string Scope { get; }

    [RelayCommand]
    private Task RevertAsync(CancellationToken ct) => _owner.RevertOneAsync(this, ct);
}

public partial class HistoryViewModel : PageViewModel
{
    private readonly AppHost _host;
    private readonly IDialogService? _dialogs;

    public HistoryViewModel(AppHost host, IDialogService? dialogs)
    {
        _host = host;
        _dialogs = dialogs;
        host.Engine.HistoryAdded += (_, _) => host.Ui.Post(Refresh);
        Refresh();
    }

    public override string Title => "Histórico";
    public override string Subtitle => "Tudo o que a app mudou, e como desfazer";

    public ObservableCollection<ActiveTweakViewModel> Active { get; } = new();
    public ObservableCollection<HistoryItem> Entries { get; } = new();
    [ObservableProperty] private bool _hasActive;
    [ObservableProperty] private bool _hasEntries;
    [ObservableProperty] private string? _message;

    public override void OnActivated() => Refresh();

    public void Refresh()
    {
        Active.Clear();
        foreach (var s in _host.Engine.ActiveSnapshots.Reverse())
            Active.Add(new ActiveTweakViewModel(this, s, _host.Engine.Find(s.TweakId)));
        Entries.Clear();
        foreach (var e in _host.Engine.History.Reverse().Take(200))
            Entries.Add(ToItem(e));
        HasActive = Active.Count > 0;
        HasEntries = Entries.Count > 0;
        RevertAllCommand.NotifyCanExecuteChanged();
    }

    public static HistoryItem ToItem(HistoryEntry e)
    {
        var (label, tone) = e.Action switch
        {
            HistoryAction.Applied => ("Aplicado", Tone.Good),
            HistoryAction.Reverted => ("Revertido", Tone.Info),
            HistoryAction.Failed => ("Falhou", Tone.Critical),
            HistoryAction.SkippedRisk => ("Ignorado (risco)", Tone.Warning),
            _ => ("Não suportado", Tone.Neutral),
        };
        return new HistoryItem(e.At.ToLocalTime().ToString("dd/MM HH:mm:ss", Format.Pt), e.TweakName, label, e.Detail, tone);
    }

    internal async Task RevertOneAsync(ActiveTweakViewModel item, CancellationToken ct)
    {
        try
        {
            var r = await Task.Run(() => _host.Engine.RevertAsync(item.Id, ct), ct);
            Message = r.Action == HistoryAction.Reverted ? $"“{item.Name}” foi revertido." : OptimizerService.Describe(r);
        }
        catch (Exception ex)
        {
            AppLog.Error("Reverter falhou", ex);
            Message = "Não foi possível reverter. Tenta de novo.";
        }
        Refresh();
    }

    private bool CanRevertAll() => HasActive;

    [RelayCommand(CanExecute = nameof(CanRevertAll))]
    private async Task RevertAllAsync(CancellationToken ct)
    {
        if (_dialogs is not null && !await _dialogs.ConfirmAsync("Reverter tudo?",
                "Todos os ajustes ativos voltam ao estado em que estavam antes do PC Max. Podes voltar a otimizar quando quiseres.",
                "Reverter tudo", danger: true))
            return;
        try
        {
            var results = await Task.Run(() => _host.Engine.RevertAllAsync(null, ct), ct);
            var failed = results.Count(r => r.Action == HistoryAction.Failed);
            Message = failed == 0 ? "Tudo revertido. O teu PC está como estava." : $"{failed} ajustes não foram revertidos: vê os detalhes abaixo.";
        }
        catch (Exception ex)
        {
            AppLog.Error("Reverter tudo falhou", ex);
            Message = "Não foi possível reverter tudo. Tenta de novo.";
        }
        Refresh();
    }
}
