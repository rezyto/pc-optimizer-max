using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcMax.App.Services;
using PcMax.Core;
using PcMax.Core.Alerts;
using PcMax.Core.Settings;
using PcMax.Core.Tweaks;

namespace PcMax.App.ViewModels;

public sealed record RiskOption(RiskLevel Level, string Label, string Description);

public partial class SettingsViewModel : PageViewModel
{
    private readonly AppHost _host;
    private readonly IDialogService? _dialogs;
    private bool _loading;

    public SettingsViewModel(AppHost host, IDialogService? dialogs)
    {
        _host = host;
        _dialogs = dialogs;
        RiskOptions = new[]
        {
            new RiskOption(RiskLevel.Safe, "Seguro", "Só alterações que não podem estragar nada. Recomendado."),
            new RiskOption(RiskLevel.Advanced, "Avançado", "Permite mexer em limites de energia e voltagens. Pede confirmação."),
            new RiskOption(RiskLevel.Extreme, "Extremo", "Para entusiastas em PCs de secretária. Pode afetar a garantia."),
        };
        Load(host.Settings.Current);
        host.Settings.Changed += (_, c) =>
        {
            // Keep the tray toggle and this page in sync.
            if (c.Old.AutoGameMode != c.New.AutoGameMode) host.Ui.Post(() => { _loading = true; AutoGameMode = c.New.AutoGameMode; _loading = false; });
        };
        if (host.Updates is { } u)
        {
            UpdateStatus = u.Status;
            u.StatusChanged += (_, _) => host.Ui.Post(() => UpdateStatus = u.Status);
        }
    }

    public override string Title => "Definições";
    public override string Subtitle => "As alterações são guardadas automaticamente";

    public IReadOnlyList<RiskOption> RiskOptions { get; }
    public ObservableCollection<string> BackgroundApps { get; } = new();
    public string Version => $"PC Max {AppInfo.Version}";
    public string RepoUrl => AppInfo.RepoUrl;

    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private bool _startMinimized;
    [ObservableProperty] private bool _closeToTray;
    [ObservableProperty] private bool _autoGameMode;
    [ObservableProperty] private bool _notificationsEnabled;
    [ObservableProperty] private bool _quietWhileGaming;
    [ObservableProperty] private bool _createRestorePoint;
    [ObservableProperty] private bool _autoUpdate;
    [ObservableProperty] private bool _shareAnonymousStats;

    [ObservableProperty] private decimal? _cpuWarning;
    [ObservableProperty] private decimal? _cpuCritical;
    [ObservableProperty] private decimal? _gpuWarning;
    [ObservableProperty] private decimal? _gpuCritical;
    [ObservableProperty] private decimal? _sustainSeconds;

    [ObservableProperty] private RiskOption _selectedRisk = null!;
    [ObservableProperty] private string _newBackgroundApp = "";
    [ObservableProperty] private string? _backgroundAppError;
    [ObservableProperty] private string _updateStatus = "";

    public bool RiskIsElevated => SelectedRisk.Level > RiskLevel.Safe;

    private void Load(AppSettings s)
    {
        _loading = true;
        StartWithWindows = s.StartWithWindows;
        StartMinimized = s.StartMinimizedToTray;
        CloseToTray = s.CloseToTray;
        AutoGameMode = s.AutoGameMode;
        NotificationsEnabled = s.NotificationsEnabled;
        QuietWhileGaming = s.QuietWhileGaming;
        CreateRestorePoint = s.CreateRestorePointBeforeChanges;
        AutoUpdate = s.AutoUpdate;
        ShareAnonymousStats = s.ShareAnonymousStats;
        LoadTemps(s.Temperatures);
        SelectedRisk = RiskOptions.First(r => r.Level == s.MaxRisk);
        BackgroundApps.Clear();
        foreach (var a in s.BackgroundAppsToSuspend) BackgroundApps.Add(a);
        _loading = false;
    }

    private void LoadTemps(TemperatureThresholds t)
    {
        CpuWarning = (decimal)t.CpuWarning;
        CpuCritical = (decimal)t.CpuCritical;
        GpuWarning = (decimal)t.GpuWarning;
        GpuCritical = (decimal)t.GpuCritical;
        SustainSeconds = t.SustainSeconds;
    }

    private void Save(Func<AppSettings, AppSettings> change)
    {
        if (_loading) return;
        _host.Settings.Update(change);
    }

    partial void OnStartWithWindowsChanged(bool value) => Save(s => s with { StartWithWindows = value });
    partial void OnStartMinimizedChanged(bool value) => Save(s => s with { StartMinimizedToTray = value });
    partial void OnCloseToTrayChanged(bool value) => Save(s => s with { CloseToTray = value });
    partial void OnAutoGameModeChanged(bool value) => Save(s => s with { AutoGameMode = value });
    partial void OnNotificationsEnabledChanged(bool value) => Save(s => s with { NotificationsEnabled = value });
    partial void OnQuietWhileGamingChanged(bool value) => Save(s => s with { QuietWhileGaming = value });
    partial void OnCreateRestorePointChanged(bool value) => Save(s => s with { CreateRestorePointBeforeChanges = value });
    partial void OnAutoUpdateChanged(bool value) => Save(s => s with { AutoUpdate = value });
    partial void OnShareAnonymousStatsChanged(bool value) => Save(s => s with { ShareAnonymousStats = value });

    partial void OnCpuWarningChanged(decimal? value) => SaveTemps();
    partial void OnCpuCriticalChanged(decimal? value) => SaveTemps();
    partial void OnGpuWarningChanged(decimal? value) => SaveTemps();
    partial void OnGpuCriticalChanged(decimal? value) => SaveTemps();
    partial void OnSustainSecondsChanged(decimal? value) => SaveTemps();

    private void SaveTemps()
    {
        if (_loading) return;
        if (CpuWarning is null || CpuCritical is null || GpuWarning is null || GpuCritical is null || SustainSeconds is null) return;
        Save(s => s with
        {
            Temperatures = s.Temperatures with
            {
                CpuWarning = (double)CpuWarning.Value,
                CpuCritical = (double)CpuCritical.Value,
                GpuWarning = (double)GpuWarning.Value,
                GpuCritical = (double)GpuCritical.Value,
                SustainSeconds = (int)SustainSeconds.Value,
            },
        });
    }

    [RelayCommand]
    private void ResetTemperatures()
    {
        var defaults = new TemperatureThresholds();
        _loading = true;
        LoadTemps(defaults);
        _loading = false;
        Save(s => s with { Temperatures = s.Temperatures with
        {
            CpuWarning = defaults.CpuWarning, CpuCritical = defaults.CpuCritical,
            GpuWarning = defaults.GpuWarning, GpuCritical = defaults.GpuCritical,
            SustainSeconds = defaults.SustainSeconds,
        } });
    }

    partial void OnSelectedRiskChanged(RiskOption? oldValue, RiskOption newValue)
    {
        OnPropertyChanged(nameof(RiskIsElevated));
        if (_loading || oldValue is null || newValue.Level == oldValue.Level) return;
        if (newValue.Level == RiskLevel.Safe || _dialogs is null)
        {
            Save(s => s with { MaxRisk = newValue.Level });
            return;
        }
        _ = ConfirmRiskAsync(oldValue, newValue);
    }

    private async Task ConfirmRiskAsync(RiskOption previous, RiskOption next)
    {
        var msg = next.Level == RiskLevel.Extreme
            ? "O modo Extremo permite ajustes que levam o hardware além dos valores de fábrica. Só para PCs de secretária com boa refrigeração, e pode afetar a garantia. Tudo continua reversível."
            : "O modo Avançado permite ajustes que mudam o comportamento do hardware (limites de energia, voltagens). Cada ajuste continua reversível e é sempre pedida confirmação.";
        var ok = await _dialogs!.ConfirmAsync($"Ativar o modo {next.Label}?",
            msg + "\n\nAs funções de overclock chegam numa versão futura.", "Ativar", danger: true);
        if (ok)
        {
            Save(s => s with { MaxRisk = next.Level });
        }
        else
        {
            _loading = true;
            SelectedRisk = previous;
            _loading = false;
        }
    }

    [RelayCommand]
    private void AddBackgroundApp()
    {
        BackgroundAppError = null;
        var name = NewBackgroundApp.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        if (name.Length == 0) return;
        if (ProcessNames.Clean(name) is null)
        {
            BackgroundAppError = "Nome inválido: escreve só o nome do programa (ex.: Spotify), sem pastas nem símbolos.";
            return;
        }
        if (ProtectedProcesses.IsProtected(name))
        {
            BackgroundAppError = $"“{name}” é essencial ao Windows ou ao anti-cheat e nunca é fechado.";
            return;
        }
        if (BackgroundApps.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase)))
        {
            BackgroundAppError = "Já está na lista.";
            return;
        }
        BackgroundApps.Add(name);
        NewBackgroundApp = "";
        SaveBackgroundApps();
    }

    [RelayCommand]
    private void RemoveBackgroundApp(string name)
    {
        BackgroundApps.Remove(name);
        SaveBackgroundApps();
    }

    private void SaveBackgroundApps() => Save(s => s with { BackgroundAppsToSuspend = BackgroundApps.ToList() });

    [RelayCommand]
    private async Task CheckUpdatesAsync(CancellationToken ct)
    {
        if (_host.Updates is null) { UpdateStatus = "Atualizações indisponíveis."; return; }
        UpdateStatus = await Task.Run(() => _host.Updates.CheckAsync(notify: false, ct), ct);
    }

    [RelayCommand]
    private void OpenRepo() => _dialogs?.OpenUrl(AppInfo.RepoUrl);

    [RelayCommand]
    private void OpenLogs()
    {
        try { Directory.CreateDirectory(_host.Paths.LogsDir); } catch { }
        _dialogs?.OpenFolder(_host.Paths.LogsDir);
    }
}
