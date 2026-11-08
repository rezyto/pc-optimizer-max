using PcMax.Core.Benchmark;
using PcMax.Core.Games;
using PcMax.Core.Network;
using PcMax.Core.Platform;

namespace PcMax.App.Services.Fakes;

public sealed class FakeGameWatcher : IGameWatcher
{
    public event EventHandler<GameEvent>? GameStarted;
    public event EventHandler<GameEvent>? GameStopped;
    public void Start() { }
    public void Stop() { }
    public void Dispose() { }

    /// <summary>For demos/screenshots: pretend a game started or stopped.</summary>
    public void SimulateStart(GameEvent e) => GameStarted?.Invoke(this, e);
    public void SimulateStop(GameEvent e) => GameStopped?.Invoke(this, e);
}

public sealed class FakeProcessBooster : IProcessBooster
{
    public string? BoostGame(int processId) => "Normal";
    public void RestoreGame(int processId, string? previousPriority) { }
    public IReadOnlyList<int> SuspendBackground(IEnumerable<string> processNames, int? excludePid = null) => new[] { 4242, 4343 };
    public void ResumeAll(IEnumerable<int> processIds) { }
}

public sealed class FakeStartupManager : IStartupManager
{
    private readonly List<StartupItem> _items = new()
    {
        new("run:Discord", "Discord", @"C:\Users\Joao\AppData\Local\Discord\Update.exe --processStart Discord.exe", "Registo (utilizador)", true, "Discord Inc."),
        new("run:Steam", "Steam", @"C:\Program Files (x86)\Steam\steam.exe -silent", "Registo (utilizador)", true, "Valve Corporation"),
        new("run:Spotify", "Spotify", @"C:\Users\Joao\AppData\Roaming\Spotify\Spotify.exe /minimized", "Registo (utilizador)", true, "Spotify AB"),
        new("run:EpicGamesLauncher", "Epic Games Launcher", @"C:\Program Files (x86)\Epic Games\Launcher\EpicGamesLauncher.exe -silent", "Registo (utilizador)", true, "Epic Games, Inc."),
        new("run:OneDrive", "Microsoft OneDrive", @"C:\Program Files\Microsoft OneDrive\OneDrive.exe /background", "Registo (utilizador)", true, "Microsoft Corporation"),
        new("folder:Teams", "Microsoft Teams", @"ms-teams.exe --system-initiated", "Pasta Arranque", false, "Microsoft Corporation"),
        new("run:RazerSynapse", "Razer Synapse", @"C:\Program Files (x86)\Razer\Synapse3\RazerSynapse.exe", "Registo (computador)", true, "Razer Inc."),
    };

    public IReadOnlyList<StartupItem> List() { lock (_items) return _items.ToList(); }

    public void SetEnabled(string id, bool enabled)
    {
        lock (_items)
        {
            var i = _items.FindIndex(x => x.Id == id);
            if (i >= 0) _items[i] = _items[i] with { Enabled = enabled };
        }
    }
}

public sealed class FakeCleaner : ICleaner
{
    private readonly Dictionary<string, CleanupCategory> _cats = new()
    {
        ["temp-user"] = new("temp-user", "Ficheiros temporários", "Restos de instalações e programas que já não são precisos.", 1_874_000_000, 6120, true),
        ["shader-cache"] = new("shader-cache", "Cache de shaders da gráfica", "É recriada sozinha; limpar ajuda após atualizar drivers.", 812_000_000, 942, false),
        ["windows-update"] = new("windows-update", "Restos do Windows Update", "Ficheiros de atualizações já instaladas.", 3_420_000_000, 210, true),
        ["recycle-bin"] = new("recycle-bin", "Reciclagem", "Ficheiros que já apagaste.", 655_000_000, 38, false),
        ["crash-dumps"] = new("crash-dumps", "Relatórios de erros", "Registos de falhas antigas de programas.", 96_000_000, 17, true),
        ["thumbnails"] = new("thumbnails", "Miniaturas", "Pré-visualizações de imagens; voltam a ser criadas.", 142_000_000, 4, true),
    };

    public async Task<IReadOnlyList<CleanupCategory>> ScanAsync(CancellationToken ct = default)
    {
        await Task.Delay(1500, ct);
        lock (_cats) return _cats.Values.ToList();
    }

    public async Task<long> CleanAsync(IEnumerable<string> categoryIds, CancellationToken ct = default)
    {
        await Task.Delay(1800, ct);
        long freed = 0;
        lock (_cats)
        {
            foreach (var id in categoryIds)
                if (_cats.TryGetValue(id, out var c))
                {
                    freed += (long)(c.Bytes * 0.93); // some files are in use
                    _cats[id] = c with { Bytes = (long)(c.Bytes * 0.07), Files = Math.Max(0, c.Files / 14) };
                }
        }
        return freed;
    }
}

public sealed class FakeAutostart : IAutostart
{
    private bool _on;
    public bool IsEnabled() => _on;
    public void Enable(string exePath) => _on = true;
    public void Disable() => _on = false;
}

public sealed class FakeRestorePoints : IRestorePoints
{
    public async Task<bool> CreateAsync(string description, CancellationToken ct = default)
    {
        await Task.Delay(900, ct);
        return true;
    }
}

/// <summary>Wi-Fi-like ping: ~24 ms with jitter, rare losses, and bufferbloat while "busy".</summary>
public sealed class FakeNetworkProbe : INetworkProbe
{
    private readonly Random _rng = new();

    public async Task<IReadOnlyList<double?>> PingAsync(string host, int count, TimeSpan interval, CancellationToken ct = default)
    {
        var baseMs = host.StartsWith("8.8", StringComparison.Ordinal) ? 31 : 22;
        var list = new List<double?>(count);
        for (var i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(interval, ct);
            if (_rng.NextDouble() < 0.02) { list.Add(null); continue; }
            var loadPenalty = FakeLoad.Busy ? 45 + _rng.NextDouble() * 60 : 0;
            list.Add(baseMs + _rng.NextDouble() * 9 + (_rng.NextDouble() < 0.08 ? 25 : 0) + loadPenalty);
        }
        return list;
    }
}

/// <summary>Lets the fake ping know the bufferbloat download is running.</summary>
public static class FakeLoad
{
    public static volatile bool Busy;
}

public sealed class FakeFrameCapture : IFrameCapture
{
    private readonly FakeState _state;
    private readonly Random _rng = new();
    public FakeFrameCapture(FakeState state) => _state = state;
    public bool IsAvailable => true;

    public async Task<IReadOnlyList<double>> CaptureAsync(int processId, TimeSpan duration, CancellationToken ct = default)
    {
        await Task.Delay(TimeSpan.FromSeconds(Math.Min(duration.TotalSeconds, 4)), ct);
        var boost = _state.IsApplied("latency.timer-resolution") ? 0.93 : 1.0;
        var frames = new List<double>();
        for (var i = 0; i < 2400; i++)
        {
            var ft = 6.9 + _rng.NextDouble() * 1.2;
            if (_rng.NextDouble() < 0.015) ft += 9 * boost;
            frames.Add(ft * (boost < 1 ? 0.98 : 1));
        }
        return frames;
    }
}
