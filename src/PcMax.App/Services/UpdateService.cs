using PcMax.Core;
using PcMax.Core.Alerts;
using PcMax.Core.Platform;
using Velopack;
using Velopack.Sources;

namespace PcMax.App.Services;

/// <summary>
/// Checks GitHub Releases with Velopack: 30 s after start, then every 6 h (if auto-update is on).
/// Downloads in the background and applies only when the app exits, never during a game.
/// </summary>
public sealed class UpdateService : IDisposable
{
    private readonly SettingsService _settings;
    private readonly INotifier _notifier;
    private readonly Func<bool> _gameActive;
    private readonly UpdateManager? _mgr;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Timer? _timer;
    private UpdateInfo? _ready;

    public UpdateService(SettingsService settings, INotifier notifier, Func<bool> gameActive)
    {
        _settings = settings;
        _notifier = notifier;
        _gameActive = gameActive;
        try
        {
            var mgr = new UpdateManager(new GithubSource(AppInfo.RepoUrl, null, false));
            if (mgr.IsInstalled) _mgr = mgr;
        }
        catch (Exception ex)
        {
            AppLog.Warn("Gestor de atualizações indisponível", ex);
        }
        Status = _mgr is null
            ? "Versão de desenvolvimento: as atualizações automáticas só funcionam na versão instalada."
            : "Ainda não foi feita nenhuma verificação.";
    }

    public bool IsInstalled => _mgr is not null;
    public bool UpdateReady => _ready is not null;
    public string Status { get; private set; }
    public event EventHandler? StatusChanged;

    public void Start()
    {
        if (_mgr is null) return;
        _timer = new Timer(async _ =>
        {
            if (!_settings.Current.AutoUpdate) return;
            try { await CheckAsync(notify: true, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception ex) { AppLog.Warn("Verificação de atualizações falhou", ex); }
        }, null, TimeSpan.FromSeconds(30), TimeSpan.FromHours(6));
    }

    /// <summary>Checks now and downloads if there is something new. Returns a message for the user.</summary>
    public async Task<string> CheckAsync(bool notify, CancellationToken ct)
    {
        if (_mgr is null) return SetStatus(Status);
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_ready is not null)
                return SetStatus($"Versão {_ready.TargetFullRelease.Version} pronta: aplica-se ao fechar a app.");
            SetStatus("A procurar atualizações…");
            var info = await _mgr.CheckForUpdatesAsync().ConfigureAwait(false);
            if (info is null)
                return SetStatus($"Tens a versão mais recente ({AppInfo.Version}). Verificado às {DateTime.Now:HH:mm}.");

            SetStatus($"A transferir a versão {info.TargetFullRelease.Version}…");
            await _mgr.DownloadUpdatesAsync(info, null, ct).ConfigureAwait(false);
            _ready = info;
            if (notify)
                _notifier.Show(new Alert("update-ready", AlertSeverity.Info, "Atualização pronta — aplica ao reiniciar",
                    $"A versão {info.TargetFullRelease.Version} do PC Max foi transferida e instala-se quando fechares a app.", DateTimeOffset.Now));
            return SetStatus($"Versão {info.TargetFullRelease.Version} pronta: aplica-se ao fechar a app.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            AppLog.Warn("Erro ao atualizar", ex);
            return SetStatus("Não foi possível verificar atualizações. Tenta mais tarde.");
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Called on exit: hand the downloaded update to Velopack's updater (it waits for us to close).</summary>
    public void ApplyOnExitIfReady(bool gameWasActive)
    {
        if (_mgr is null || _ready is null) return;
        if (gameWasActive || _gameActive())
        {
            AppLog.Info("Atualização adiada: sessão de jogo ativa.");
            return;
        }
        try
        {
            _mgr.WaitExitThenApplyUpdates(_ready.TargetFullRelease, silent: true, restart: false);
        }
        catch (Exception ex)
        {
            AppLog.Error("Falha ao preparar atualização", ex);
        }
    }

    private string SetStatus(string s)
    {
        Status = s;
        StatusChanged?.Invoke(this, EventArgs.Empty);
        return s;
    }

    public void Dispose() => _timer?.Dispose();
}
