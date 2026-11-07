using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcMax.App.Services;
using PcMax.Core.Platform;

namespace PcMax.App.ViewModels;

public partial class CleanupItemViewModel : ViewModelBase
{
    private readonly Action _changed;

    public CleanupItemViewModel(CleanupCategory c, Action changed)
    {
        Category = c;
        _changed = changed;
        _isSelected = c.SafeByDefault;
    }

    public CleanupCategory Category { get; }
    public string Name => Category.Name;
    public string Description => Category.Description;
    public string Size => Format.Bytes(Category.Bytes);
    public string FilesText => Category.Files == 1 ? "1 ficheiro" : $"{Category.Files:N0} ficheiros".Replace(',', ' ');
    public bool Recommended => Category.SafeByDefault;

    [ObservableProperty] private bool _isSelected;
    partial void OnIsSelectedChanged(bool value) => _changed();
}

public partial class StartupItemViewModel : ViewModelBase
{
    private readonly IStartupManager _manager;
    private bool _reverting;

    public StartupItemViewModel(StartupItem item, IStartupManager manager)
    {
        Item = item;
        _manager = manager;
        _enabled = item.Enabled;
    }

    public StartupItem Item { get; }
    public string Name => Item.Name;
    public string Publisher => Item.Publisher ?? "Editor desconhecido";
    public string Location => Item.Location;
    public string Command => Item.Command;

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string? _error;

    partial void OnEnabledChanged(bool value)
    {
        if (_reverting) return;
        Error = null;
        _ = ApplyAsync(value);
    }

    private async Task ApplyAsync(bool value)
    {
        try
        {
            await Task.Run(() => _manager.SetEnabled(Item.Id, value));
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Falha ao alterar arranque de {Item.Name}", ex);
            _reverting = true;
            Enabled = !value;
            _reverting = false;
            Error = "Não foi possível alterar.";
        }
    }
}

public partial class CleanupViewModel : PageViewModel
{
    private readonly AppHost _host;
    private readonly IDialogService? _dialogs;
    private bool _startupLoaded;

    public CleanupViewModel(AppHost host, IDialogService? dialogs)
    {
        _host = host;
        _dialogs = dialogs;
    }

    public override string Title => "Limpeza e arranque";
    public override string Subtitle => "Liberta espaço e escolhe o que abre com o Windows";

    public ObservableCollection<CleanupItemViewModel> Categories { get; } = new();
    public ObservableCollection<StartupItemViewModel> StartupItems { get; } = new();

    [ObservableProperty] private bool _hasScan;
    [ObservableProperty] private string _selectedTotal = "0 B";
    [ObservableProperty] private bool _hasSelection;
    [ObservableProperty] private string? _cleanResult;
    [ObservableProperty] private string? _startupSummary;

    public override void OnActivated()
    {
        if (!_startupLoaded) _ = LoadStartupAsync();
    }

    [RelayCommand]
    private async Task LoadStartupAsync()
    {
        _startupLoaded = true;
        try
        {
            var items = await Task.Run(() => _host.Platform.Startup.List());
            StartupItems.Clear();
            foreach (var i in items.OrderByDescending(i => i.Enabled).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var vm = new StartupItemViewModel(i, _host.Platform.Startup);
                vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(StartupItemViewModel.Enabled)) UpdateStartupSummary(); };
                StartupItems.Add(vm);
            }
            UpdateStartupSummary();
        }
        catch (Exception ex)
        {
            AppLog.Warn("Falha ao listar programas de arranque", ex);
            StartupSummary = "Não foi possível ler os programas de arranque.";
        }
    }

    private void UpdateStartupSummary()
    {
        var on = StartupItems.Count(i => i.Enabled);
        StartupSummary = $"{on} de {StartupItems.Count} programas abrem com o Windows. Desligar não apaga nada.";
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task ScanAsync(CancellationToken ct)
    {
        CleanResult = null;
        try
        {
            var cats = await Task.Run(() => _host.Platform.Cleaner.ScanAsync(ct), ct);
            Categories.Clear();
            foreach (var c in cats.OrderByDescending(c => c.Bytes))
                Categories.Add(new CleanupItemViewModel(c, UpdateTotal));
            HasScan = true;
            UpdateTotal();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AppLog.Error("Análise de limpeza falhou", ex);
            CleanResult = "Não foi possível analisar o disco.";
        }
    }

    private void UpdateTotal()
    {
        var sel = Categories.Where(c => c.IsSelected).ToList();
        SelectedTotal = Format.Bytes(sel.Sum(c => c.Category.Bytes));
        HasSelection = sel.Count > 0;
        CleanCommand.NotifyCanExecuteChanged();
    }

    private bool CanClean() => HasSelection;

    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task CleanAsync(CancellationToken ct)
    {
        var sel = Categories.Where(c => c.IsSelected).ToList();
        if (sel.Count == 0) return;
        if (_dialogs is not null && !await _dialogs.ConfirmAsync("Limpar ficheiros?",
                $"Vão ser apagados cerca de {SelectedTotal} em {sel.Count} categorias. Ficheiros em uso são ignorados. Isto não pode ser desfeito.",
                "Limpar"))
            return;
        try
        {
            var freed = await Task.Run(() => _host.Platform.Cleaner.CleanAsync(sel.Select(c => c.Category.Id), ct), ct);
            CleanResult = $"Pronto! Libertaste {Format.Bytes(freed)}.";
            await ScanAsync(ct);
            CleanResult = $"Pronto! Libertaste {Format.Bytes(freed)}.";
        }
        catch (OperationCanceledException) { CleanResult = "Limpeza cancelada."; }
        catch (Exception ex)
        {
            AppLog.Error("Limpeza falhou", ex);
            CleanResult = "A limpeza falhou a meio. Nada de importante foi afetado.";
        }
    }
}
