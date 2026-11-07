using CommunityToolkit.Mvvm.ComponentModel;
using PcMax.App.Services;
using PcMax.Core;

namespace PcMax.App.ViewModels;

public sealed record NavItem(string Title, string IconKey, PageViewModel Page);

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly AppHost _host;

    public MainWindowViewModel(AppHost host, IDialogService? dialogs)
    {
        _host = host;
        Dashboard = new DashboardViewModel(host, Navigate);
        Doctor = new DoctorViewModel(host, dialogs);
        Games = new GamesViewModel(host, dialogs);
        Network = new NetworkViewModel(host);
        Cleanup = new CleanupViewModel(host, dialogs);
        History = new HistoryViewModel(host, dialogs);
        Settings = new SettingsViewModel(host, dialogs);
        Dashboard.SetDoctorPage(() => Doctor);

        Pages = new[]
        {
            new NavItem("Painel", "Icon.Dashboard", Dashboard),
            new NavItem("Médico do PC", "Icon.Doctor", Doctor),
            new NavItem("Jogos", "Icon.Games", Games),
            new NavItem("Rede", "Icon.Network", Network),
            new NavItem("Limpeza e arranque", "Icon.Clean", Cleanup),
            new NavItem("Histórico", "Icon.History", History),
            new NavItem("Definições", "Icon.Settings", Settings),
        };
        _selected = Pages[0];
        host.GameStatusChanged += (_, _) => UpdateGameStatus();
        UpdateGameStatus();
    }

    public DashboardViewModel Dashboard { get; }
    public DoctorViewModel Doctor { get; }
    public GamesViewModel Games { get; }
    public NetworkViewModel Network { get; }
    public CleanupViewModel Cleanup { get; }
    public HistoryViewModel History { get; }
    public SettingsViewModel Settings { get; }

    public IReadOnlyList<NavItem> Pages { get; }
    public string Version => "v" + AppInfo.Version;

    [ObservableProperty] private NavItem _selected;
    [ObservableProperty] private string _gameStatus = "";
    [ObservableProperty] private bool _isGameActive;

    public PageViewModel CurrentPage => Selected.Page;

    partial void OnSelectedChanged(NavItem value)
    {
        OnPropertyChanged(nameof(CurrentPage));
        value?.Page.OnActivated();
    }

    public void Navigate(PageViewModel page)
    {
        var item = Pages.FirstOrDefault(p => p.Page == page);
        if (item is not null) Selected = item;
    }

    private void UpdateGameStatus()
    {
        GameStatus = _host.GameStatusText;
        IsGameActive = _host.IsGameActive;
    }
}
