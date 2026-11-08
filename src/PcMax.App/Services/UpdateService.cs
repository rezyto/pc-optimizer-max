using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using PcMax.Core;
using PcMax.Core.Alerts;
using PcMax.Core.Platform;
using PcMax.Core.Settings;
using PcMax.Core.Updates;

namespace PcMax.App.Services;

/// <summary>
/// Updates from this repository's GitHub Releases (no third-party updater):
/// <list type="number">
/// <item>asks <c>api.github.com/repos/{owner}/{repo}/releases/latest</c> (30 s after start, then every 6 h when
/// auto-update is on, or when the user presses "Procurar atualizações agora");</item>
/// <item>downloads <c>PcMax-Setup-&lt;ver&gt;.exe</c> and its <c>.sha256</c> into <see cref="AppPaths.UpdatesDir"/>
/// (admin-only), and keeps it only if the SHA-256 matches (fail closed);</item>
/// <item>if the running PcMax.exe is Authenticode-signed, the installer must also carry a valid signature from the
/// same certificate;</item>
/// <item>installs only when the user says so ("Instalar agora") or at the next start, before the game watcher runs.</item>
/// </list>
/// Nothing is checked, downloaded or installed while a game session is active.
/// </summary>
public sealed class UpdateService : IDisposable
{
    private static readonly string[] InstallerArgs = { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/CLOSEAPPLICATIONS" };
    private static readonly Lazy<HttpClient> Http = new(CreateClient);

    private readonly SettingsService _settings;
    private readonly INotifier _notifier;
    private readonly Func<bool> _gameActive;
    private readonly AppPaths _paths;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ReleaseVersion _current;
    private Timer? _timer;
    private Pending? _ready;

    private sealed record Pending(ReleaseVersion Version, string InstallerPath, string Sha256);

    public UpdateService(SettingsService settings, INotifier notifier, Func<bool> gameActive, AppPaths? paths = null)
    {
        _settings = settings;
        _notifier = notifier;
        _gameActive = gameActive;
        _paths = paths ?? PlatformFactory.DefaultPaths();
        ReleaseVersion.TryParse(AppInfo.Version, out _current);
        IsInstalled = PlatformFactory.IsInstalledCopy;
        Status = IsInstalled
            ? "Ainda não foi feita nenhuma verificação."
            : "Versão de desenvolvimento: as atualizações só funcionam na versão instalada.";
        if (IsInstalled)
        {
            try { _ready = FindPending(); }
            catch (Exception ex) { AppLog.Warn("Não foi possível ler as atualizações transferidas", ex); }
            if (_ready is not null) Status = ReadyText(_ready);
        }
    }

    /// <summary>Only the copy installed in Program Files updates itself.</summary>
    public bool IsInstalled { get; }
    public bool UpdateReady => _ready is not null;
    public string Status { get; private set; }
    public event EventHandler? StatusChanged;

    /// <summary>Set by the app: closes it cleanly (reverting the game session) so the installer can replace the files.</summary>
    public Func<Task>? ExitApp { get; set; }

    public void Start()
    {
        if (!IsInstalled || _timer is not null) return;
        _timer = new Timer(async _ =>
        {
            if (!_settings.Current.AutoUpdate || _gameActive()) return;
            try { await CheckAsync(notify: true, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception ex) { AppLog.Warn("Verificação de atualizações falhou", ex); }
        }, null, TimeSpan.FromSeconds(30), TimeSpan.FromHours(6));
    }

    /// <summary>Checks now and downloads if there is something new. Returns a message for the user.</summary>
    public async Task<string> CheckAsync(bool notify, CancellationToken ct)
    {
        if (!IsInstalled) return SetStatus(Status);
        if (_gameActive()) return SetStatus("Durante um jogo não se procuram atualizações. Tenta depois de fechares o jogo.");
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_ready is not null) return SetStatus(ReadyText(_ready));
            SetStatus("A procurar atualizações…");

            var json = await GetStringCappedAsync(ReleaseFeed.LatestApiUrl(AppInfo.RepoOwner, AppInfo.RepoName),
                ReleaseFeed.MaxJsonBytes, "application/vnd.github+json", ct).ConfigureAwait(false);
            var release = ReleaseFeed.TryParseLatest(json, AppInfo.RepoOwner, AppInfo.RepoName, _current, out var reason);
            if (release is null)
            {
                AppLog.Info($"Atualizações: nada a fazer ({reason}).");
                return SetStatus($"Tens a versão mais recente ({AppInfo.Version}). Verificado às {DateTime.Now:HH:mm}.");
            }

            SetStatus($"A transferir a versão {release.Version}…");
            var pending = await DownloadAsync(release, ct).ConfigureAwait(false);
            if (pending is null)
                return SetStatus("A atualização transferida não passou na verificação de segurança e foi apagada. Tenta mais tarde.");
            _ready = pending;
            if (notify) NotifyReady(pending);
            return SetStatus(ReadyText(pending));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            AppLog.Warn("Erro ao procurar atualizações", ex);
            return SetStatus("Não foi possível verificar atualizações. Tenta mais tarde.");
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Runs the downloaded installer silently and closes the app. Returns a message when it cannot.</summary>
    public async Task<string> InstallNowAsync()
    {
        var pending = _ready;
        if (pending is null) return SetStatus("Não há nenhuma atualização transferida.");
        if (_gameActive()) return SetStatus("A atualização instala-se depois de fechares o jogo.");
        if (!Launch(pending))
        {
            _ready = null;
            return SetStatus("A atualização não passou na verificação de segurança e foi apagada. Procura de novo mais tarde.");
        }
        SetStatus($"A instalar a versão {pending.Version}… O PC Max volta a abrir sozinho.");
        if (ExitApp is { } exit)
        {
            try { await exit().ConfigureAwait(false); }
            catch (Exception ex) { AppLog.Error("Falha ao fechar para instalar a atualização", ex); }
        }
        return Status;
    }

    /// <summary>
    /// At start-up, before the game watcher: installs an update downloaded in a previous run. Tried once per version,
    /// so a broken installer can never put the app in a loop. Returns true when the installer was started (the caller exits).
    /// </summary>
    public bool TryInstallPendingAtStartup()
    {
        if (!IsInstalled || _ready is not { } pending || _gameActive()) return false;
        var marker = pending.InstallerPath + ".tentado";
        try
        {
            if (File.Exists(marker)) return false; // already tried once: wait for the user to press "Instalar agora"
            AtomicFile.WriteAllText(marker, DateTimeOffset.Now.ToString("O"));
        }
        catch (Exception ex)
        {
            AppLog.Warn("Não foi possível marcar a tentativa de atualização", ex);
            return false;
        }
        AppLog.Info($"A instalar a atualização {pending.Version} transferida antes.");
        if (Launch(pending)) return true;
        _ready = null;
        return false;
    }

    // ------------------------------------------------------------------ download and verification

    private async Task<Pending?> DownloadAsync(ReleaseCandidate release, CancellationToken ct)
    {
        Directory.CreateDirectory(_paths.UpdatesDir);
        var checksumText = await GetStringCappedAsync(release.ChecksumUrl, 4096, "application/octet-stream", ct).ConfigureAwait(false);
        var expected = ReleaseAssets.ParseChecksum(checksumText, release.InstallerName)
            ?? throw new InvalidDataException("Ficheiro .sha256 inválido.");

        var final = Path.Combine(_paths.UpdatesDir, release.InstallerName);
        var partial = final + ".partial";
        DeleteQuietly(partial);
        string actual;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, release.InstallerUrl);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
            using var resp = await Http.Value.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            if (resp.Content.Headers.ContentLength is > ReleaseFeed.MaxInstallerBytes) throw new InvalidDataException("Instalador demasiado grande.");

            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var dst = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                var buf = new byte[81920];
                long total = 0;
                int n;
                while ((n = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
                {
                    total += n;
                    if (total > ReleaseFeed.MaxInstallerBytes) throw new InvalidDataException("Instalador demasiado grande.");
                    if (_gameActive()) throw new OperationCanceledException("Transferência interrompida: começou um jogo.");
                    sha.AppendData(buf, 0, n);
                    await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                }
            }
            actual = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
        }
        catch
        {
            DeleteQuietly(partial);
            throw;
        }

        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            AppLog.Warn($"Atualização {release.Version}: SHA-256 não coincide (esperado {expected}, obtido {actual}). Apagada.");
            DeleteQuietly(partial);
            return null;
        }
        if (PlatformFactory.CheckUpdateSignature(partial) is { } sigProblem)
        {
            AppLog.Warn($"Atualização {release.Version}: {sigProblem}. Apagada.");
            DeleteQuietly(partial);
            return null;
        }

        DeleteQuietly(final);
        File.Move(partial, final);
        // Our own record of the verified hash, kept next to it in the admin-only folder, for the install at next start.
        AtomicFile.WriteAllText(final + ".sha256", $"{actual}  {release.InstallerName}\n");
        AppLog.Info($"Atualização {release.Version} transferida e verificada ({actual}).");
        return new Pending(release.Version, final, actual);
    }

    /// <summary>Re-checks hash and signature right before running the installer, then starts it. Deletes it if it fails.</summary>
    private bool Launch(Pending pending)
    {
        try
        {
            string actual;
            using (var fs = new FileStream(pending.InstallerPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                actual = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
            if (!string.Equals(actual, pending.Sha256, StringComparison.Ordinal))
                throw new InvalidDataException("SHA-256 do instalador mudou.");
            if (PlatformFactory.CheckUpdateSignature(pending.InstallerPath) is { } problem)
                throw new InvalidDataException(problem);

            // The app is elevated, so the installer (PrivilegesRequired=admin) starts without another UAC prompt.
            var psi = new ProcessStartInfo(pending.InstallerPath)
            {
                UseShellExecute = false,
                WorkingDirectory = _paths.UpdatesDir,
            };
            foreach (var a in InstallerArgs) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("O instalador não arrancou.");
            AppLog.Info($"Instalador da versão {pending.Version} iniciado (PID {p.Id}).");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error($"Atualização {pending.Version} não instalada", ex);
            RemovePending(pending);
            return false;
        }
    }

    /// <summary>The newest verified installer already in the updates folder (from a previous run); older ones are deleted.</summary>
    private Pending? FindPending()
    {
        if (!Directory.Exists(_paths.UpdatesDir)) return null;
        Pending? best = null;
        foreach (var file in Directory.EnumerateFiles(_paths.UpdatesDir, "PcMax-Setup-*.exe"))
        {
            var name = Path.GetFileName(file);
            var verText = name["PcMax-Setup-".Length..^".exe".Length];
            if (!ReleaseVersion.TryParse(verText, out var v) || ReleaseAssets.InstallerName(v) != name || v <= _current)
            {
                RemoveFiles(file);
                continue;
            }
            string? digest = null;
            try { digest = ReleaseAssets.ParseChecksum(AtomicFile.ReadAllTextCapped(file + ".sha256", 4096), name); }
            catch (IOException) { }
            if (digest is null) { RemoveFiles(file); continue; }
            if (best is null || v > best.Version)
            {
                if (best is not null) RemoveFiles(best.InstallerPath);
                best = new Pending(v, file, digest);
            }
            else RemoveFiles(file);
        }
        return best;
    }

    private void RemovePending(Pending p) => RemoveFiles(p.InstallerPath);

    private static void RemoveFiles(string installer)
    {
        DeleteQuietly(installer);
        DeleteQuietly(installer + ".sha256");
        DeleteQuietly(installer + ".tentado");
    }

    private static void DeleteQuietly(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { AppLog.Warn($"Não foi possível apagar {path}", ex); }
    }

    // ------------------------------------------------------------------ http

    private static HttpClient CreateClient()
    {
        // Default TLS settings (OS-negotiated, certificate validation on). Redirects stay on HTTPS (https→http is never followed).
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            AutomaticDecompression = System.Net.DecompressionMethods.All,
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PcMax-Updater");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    private static async Task<string> GetStringCappedAsync(Uri url, int maxBytes, string accept, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        using var resp = await Http.Value.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        if (resp.Content.Headers.ContentLength > maxBytes) throw new InvalidDataException("Resposta demasiado grande.");
        await using var s = await resp.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
        using var ms = new MemoryStream();
        var buf = new byte[16384];
        int n;
        while ((n = await s.ReadAsync(buf, cts.Token).ConfigureAwait(false)) > 0)
        {
            if (ms.Length + n > maxBytes) throw new InvalidDataException("Resposta demasiado grande.");
            ms.Write(buf, 0, n);
        }
        return System.Text.Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }

    // ------------------------------------------------------------------ ui

    private static string ReadyText(Pending p) =>
        $"A versão {p.Version} está pronta. Instala agora ou no próximo arranque da app.";

    private void NotifyReady(Pending p)
    {
        var alert = new Alert("update-ready", AlertSeverity.Info, "Atualização pronta",
            $"A versão {p.Version} do PC Max foi transferida e verificada.", DateTimeOffset.Now);
        if (_notifier is IActionNotifier actions)
            actions.Show(alert, "Instalar agora", () => _ = InstallNowAsync());
        else
            _notifier.Show(alert);
    }

    private string SetStatus(string s)
    {
        Status = s;
        StatusChanged?.Invoke(this, EventArgs.Empty);
        return s;
    }

    public void Dispose() => _timer?.Dispose();
}

/// <summary>A notifier whose pop-up can carry one button.</summary>
public interface IActionNotifier : INotifier
{
    /// <summary>Thread-safe. <paramref name="action"/> runs on the UI thread when the button is pressed.</summary>
    void Show(Alert alert, string actionText, Action action);
}
