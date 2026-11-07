using PcMax.Core.Platform;

namespace PcMax.App.Services;

/// <summary>Creates a Windows restore point before the first change of a session (if enabled in settings).</summary>
public sealed class ChangeGuard
{
    private readonly IRestorePoints _restore;
    private readonly SettingsService _settings;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _createdThisSession;

    public ChangeGuard(IRestorePoints restore, SettingsService settings)
    {
        _restore = restore;
        _settings = settings;
    }

    /// <summary>Returns a message for the user when the restore point could not be created, otherwise null.</summary>
    public async Task<string?> EnsureRestorePointAsync(CancellationToken ct = default)
    {
        if (!_settings.Current.CreateRestorePointBeforeChanges) return null;
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_createdThisSession) return null;
            bool ok;
            try { ok = await _restore.CreateAsync("PC Max - antes de otimizar", ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                AppLog.Warn("Falha ao criar ponto de restauro", ex);
                ok = false;
            }
            if (ok) { _createdThisSession = true; return null; }
            return "Não foi possível criar um ponto de restauro (o Restauro do Sistema pode estar desligado). Tudo continua reversível no Histórico.";
        }
        finally
        {
            _lock.Release();
        }
    }
}
