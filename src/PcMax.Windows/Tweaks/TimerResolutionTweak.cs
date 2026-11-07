using System.Globalization;
using System.Runtime.Versioning;
using PcMax.Core.Tweaks;
using PcMax.Windows.Native;

namespace PcMax.Windows.Tweaks;

/// <summary>
/// Requests a 0.5 ms system timer resolution while a game runs. The request belongs to our process
/// and is held by <see cref="TimerResolutionState"/>; Windows drops it automatically if we exit or crash.
/// Since Windows 10 2004 the request only affects the requesting process, unless (Windows 11 /
/// Server 2022+) HKLM\...\Session Manager\kernel GlobalTimerResolutionRequests=1 is set. We never set it.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class TimerResolutionTweak : ITweak
{
    public string Id => "latency.timer-resolution";
    public string Name => "Temporizador do Windows mais preciso durante o jogo";
    public string Description =>
        "Enquanto jogas, pede ao Windows um temporizador de 0,5 ms em vez dos habituais 15,6 ms, o que pode reduzir pequenas " +
        "variações no tempo de cada imagem. Termina sozinho quando o jogo fecha. Limitação: desde o Windows 10 (versão 2004) " +
        "este pedido só afeta a própria app, a não ser que a opção avançada do Windows \"GlobalTimerResolutionRequests\" esteja " +
        "ligada (Windows 11); a app não altera essa opção. Por isso, em muitos PCs o efeito no jogo é nulo, e o teste de FPS mostra-o.";
    public RiskLevel Risk => RiskLevel.Safe;
    public TweakCategory Category => TweakCategory.Latency;
    public bool GameSessionOnly => true;

    public Task<bool> IsSupportedAsync(CancellationToken ct = default)
    {
        try { return Task.FromResult(NativeMethods.NtQueryTimerResolution(out _, out _, out _) >= 0); }
        catch { return Task.FromResult(false); }
    }

    public Task<bool> IsAppliedAsync(CancellationToken ct = default) => Task.FromResult(TimerResolutionState.IsRequested);

    public Task<TweakSnapshot> ApplyAsync(CancellationToken ct = default)
    {
        var current = TimerResolutionState.Request();
        return Task.FromResult(TweakSnapshot.Create(Id, new Dictionary<string, string?>
        {
            // Informational only: the OS restores the resolution itself once no process requests it.
            ["previousResolution100ns"] = current?.ToString(CultureInfo.InvariantCulture),
        }));
    }

    public Task RevertAsync(TweakSnapshot snapshot, CancellationToken ct = default)
    {
        // After a crash the request died with the old process; Release() is then a no-op.
        TimerResolutionState.Release();
        return Task.CompletedTask;
    }
}

/// <summary>Process-wide holder of our single timer resolution request.</summary>
[SupportedOSPlatform("windows")]
internal static class TimerResolutionState
{
    private const uint Desired = 5000; // 0.5 ms in 100 ns units
    private static readonly object Gate = new();
    private static uint? _requested;

    public static bool IsRequested { get { lock (Gate) return _requested is not null; } }

    /// <summary>Requests the resolution. Returns the resolution that was current before.</summary>
    public static uint? Request()
    {
        lock (Gate)
        {
            if (NativeMethods.NtQueryTimerResolution(out _, out var finest, out var current) < 0)
                throw new InvalidOperationException("Não foi possível ler a resolução do temporizador.");
            if (_requested is not null) return current;
            var want = Math.Max(Desired, finest);
            var status = NativeMethods.NtSetTimerResolution(want, true, out var actual);
            if (status < 0) throw new InvalidOperationException($"NtSetTimerResolution falhou (0x{status:X8}).");
            _requested = want;
            Log.Info($"Resolução do temporizador pedida: {want / 10000.0:0.###} ms (atual {actual / 10000.0:0.###} ms)");
            return current;
        }
    }

    public static void Release()
    {
        lock (Gate)
        {
            if (_requested is not { } r) return;
            NativeMethods.NtSetTimerResolution(r, false, out _);
            _requested = null;
            Log.Info("Resolução do temporizador libertada.");
        }
    }
}
