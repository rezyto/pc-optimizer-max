using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcMax.App.Services;

namespace PcMax.App.ViewModels;

/// <summary>Backs the tray icon menu. Actions that need windows are passed in by the App.</summary>
public partial class TrayViewModel : ViewModelBase
{
    private readonly SettingsService _settings;
    private readonly Action _open;
    private readonly Func<Task> _revertAll;
    private readonly Func<Task> _exit;

    public TrayViewModel(SettingsService settings, IUiDispatcher ui, Action open, Func<Task> revertAll, Func<Task> exit)
    {
        _settings = settings;
        _open = open;
        _revertAll = revertAll;
        _exit = exit;
        _autoGameMode = settings.Current.AutoGameMode;
        settings.Changed += (_, c) => ui.Post(() => AutoGameMode = c.New.AutoGameMode);
    }

    [ObservableProperty] private bool _autoGameMode;
    [ObservableProperty] private string _toolTip = "PC Max";

    [RelayCommand] private void Open() => _open();
    [RelayCommand] private void ToggleAutoGameMode() => _settings.Update(s => s with { AutoGameMode = !s.AutoGameMode });
    [RelayCommand] private Task RevertAll() => _revertAll();
    [RelayCommand] private Task Exit() => _exit();
}
