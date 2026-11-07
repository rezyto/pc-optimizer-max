using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcMax.App.Services;
using PcMax.Core.Games;
using PcMax.Core.Tweaks;

namespace PcMax.App.ViewModels;

public partial class GameTweakViewModel : ViewModelBase
{
    private readonly GameProfileViewModel _owner;
    private readonly AppHost _host;

    public GameTweakViewModel(GameProfileViewModel owner, ITweak tweak, bool selected, AppHost host)
    {
        _owner = owner;
        _host = host;
        Id = tweak.Id;
        Name = tweak.Name;
        Description = tweak.Description;
        _isSelected = selected;
    }

    public string Id { get; }
    public string Name { get; }
    public string Description { get; }

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string? _testResult;
    [ObservableProperty] private string? _testProgress;

    partial void OnIsSelectedChanged(bool value) => _owner.Save();

    public bool CanTest => _owner.IsRunning && _host.Benchmark.IsAvailable;

    internal void RefreshCanTest()
    {
        OnPropertyChanged(nameof(CanTest));
        TestCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanTest), IncludeCancelCommand = true)]
    private async Task TestAsync(CancellationToken ct)
    {
        if (_owner.ProcessId is not { } pid) return;
        TestResult = null;
        var progress = new Progress<string>(m => TestProgress = m);
        try
        {
            TestResult = await Task.Run(() => _host.Benchmark.TestTweakAsync(pid, Id, progress, ct), ct);
        }
        catch (OperationCanceledException) { TestResult = "Teste cancelado. Tudo ficou como estava."; }
        catch (Exception ex)
        {
            AppLog.Error("Teste de ganho falhou", ex);
            TestResult = "O teste falhou. Nada ficou alterado.";
        }
        finally { TestProgress = null; }
    }
}

public partial class GameProfileViewModel : ViewModelBase
{
    private readonly Action<GameProfileViewModel> _save;
    private readonly Action<GameProfileViewModel> _remove;
    private bool _loading = true;

    public GameProfileViewModel(GameProfile p, AppHost host, Action<GameProfileViewModel> save, Action<GameProfileViewModel> remove)
    {
        _save = save;
        _remove = remove;
        ExecutableName = p.ExecutableName;
        DisplayName = p.DisplayName;
        Enabled = p.Enabled;
        HighPriority = p.HighPriority;
        SuspendBackgroundApps = p.SuspendBackgroundApps;
        foreach (var t in host.Engine.Tweaks.Where(t => t.GameSessionOnly).OrderBy(t => t.Name))
            Tweaks.Add(new GameTweakViewModel(this, t, p.TweakIds.Contains(t.Id, StringComparer.Ordinal), host));
        _loading = false;
    }

    public string ExecutableName { get; }
    public string DisplayName { get; }
    public string Initials => string.Concat(DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0])));
    public ObservableCollection<GameTweakViewModel> Tweaks { get; } = new();

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private bool _highPriority;
    [ObservableProperty] private bool _suspendBackgroundApps;
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private int? _processId;

    public bool IsRunning => ProcessId is not null;

    partial void OnEnabledChanged(bool value) => Save();
    partial void OnHighPriorityChanged(bool value) => Save();
    partial void OnSuspendBackgroundAppsChanged(bool value) => Save();

    partial void OnProcessIdChanged(int? value)
    {
        OnPropertyChanged(nameof(IsRunning));
        foreach (var t in Tweaks) t.RefreshCanTest();
    }

    internal void Save()
    {
        if (!_loading) _save(this);
    }

    public GameProfile ToProfile() => new()
    {
        ExecutableName = ExecutableName,
        DisplayName = DisplayName,
        Enabled = Enabled,
        HighPriority = HighPriority,
        SuspendBackgroundApps = SuspendBackgroundApps,
        TweakIds = Tweaks.Where(t => t.IsSelected).Select(t => t.Id).ToList(),
    };

    [RelayCommand]
    private void Remove() => _remove(this);
}

public sealed record RunningGameItem(string Name, string ExecutableName, int ProcessId);

public partial class GamesViewModel : PageViewModel
{
    private readonly AppHost _host;
    private readonly IDialogService? _dialogs;

    public GamesViewModel(AppHost host, IDialogService? dialogs)
    {
        _host = host;
        _dialogs = dialogs;
        foreach (var p in host.Settings.Current.Games)
            Profiles.Add(Create(p));
        KnownGames = Core.Games.KnownGames.All.Values.Distinct().OrderBy(x => x, StringComparer.CurrentCulture).ToList();
        host.RunningGamesChanged += (_, _) => RefreshRunning();
        RefreshRunning();
    }

    public override string Title => "Jogos";
    public override string Subtitle => "Perfis por jogo: ativam ao abrir o jogo e revertem ao fechar";

    public ObservableCollection<GameProfileViewModel> Profiles { get; } = new();
    public ObservableCollection<RunningGameItem> RunningWithoutProfile { get; } = new();
    public IReadOnlyList<string> KnownGames { get; }
    public string KnownGamesText => string.Join(" · ", KnownGames);
    public bool HasProfiles => Profiles.Count > 0;
    public bool CaptureAvailable => _host.Benchmark.IsAvailable;
    public string CaptureNote => CaptureAvailable
        ? "“Testar ganho” mede os FPS com o jogo aberto, antes e depois do ajuste, e só o mantém se melhorar mesmo."
        : "Para medir FPS (“Testar ganho”) é preciso o PresentMon, uma ferramenta gratuita da Intel. Os perfis funcionam sem ele.";

    [ObservableProperty] private string? _message;

    public override void OnActivated() => RefreshRunning();

    private GameProfileViewModel Create(GameProfile p) => new(p, _host, Save, RemoveProfile);

    private void RefreshRunning()
    {
        var running = _host.RunningGames;
        foreach (var p in Profiles)
            p.ProcessId = running.FirstOrDefault(r => string.Equals(r.ExecutableName, p.ExecutableName, StringComparison.OrdinalIgnoreCase))?.ProcessId;
        RunningWithoutProfile.Clear();
        foreach (var r in running.Where(r => Profiles.All(p => !string.Equals(p.ExecutableName, r.ExecutableName, StringComparison.OrdinalIgnoreCase))))
            RunningWithoutProfile.Add(new RunningGameItem(r.DisplayName ?? Core.Games.KnownGames.NameOf(r.ExecutableName) ?? r.ExecutableName, r.ExecutableName, r.ProcessId));
    }

    private void Save(GameProfileViewModel _) => _host.Settings.Update(s => s with { Games = Profiles.Select(p => p.ToProfile()).ToList() });

    private void RemoveProfile(GameProfileViewModel p)
    {
        Profiles.Remove(p);
        Save(p);
        OnPropertyChanged(nameof(HasProfiles));
        RefreshRunning();
    }

    /// <summary>Adds a profile for an executable (or returns the existing one).</summary>
    public GameProfileViewModel AddProfile(string exeName, string? displayName = null)
    {
        var existing = Profiles.FirstOrDefault(p => string.Equals(p.ExecutableName, exeName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;
        var name = displayName ?? Core.Games.KnownGames.NameOf(exeName) ?? Path.GetFileNameWithoutExtension(exeName);
        var vm = Create(new GameProfile { ExecutableName = exeName, DisplayName = name });
        vm.IsExpanded = true;
        Profiles.Insert(0, vm);
        Save(vm);
        OnPropertyChanged(nameof(HasProfiles));
        RefreshRunning();
        return vm;
    }

    [RelayCommand]
    private async Task AddGameAsync()
    {
        if (_dialogs is null) return;
        var path = await _dialogs.PickExecutableAsync();
        if (string.IsNullOrEmpty(path)) return;
        var exe = Path.GetFileName(path);
        if (Core.Settings.ProtectedProcesses.IsProtected(exe))
        {
            Message = $"“{exe}” é um processo do sistema e não pode ser um jogo.";
            return;
        }
        var vm = AddProfile(exe);
        Message = $"“{vm.DisplayName}” adicionado. O modo jogo ativa quando o abrires.";
    }

    [RelayCommand]
    private void Customize(RunningGameItem item) => AddProfile(item.ExecutableName, item.Name);
}
