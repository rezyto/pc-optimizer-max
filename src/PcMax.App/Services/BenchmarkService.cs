using PcMax.Core.Benchmark;
using PcMax.Core.Tweaks;

namespace PcMax.App.Services;

/// <summary>Measures a tweak in a running game: capture frames, apply, capture again, keep only if it really helped.</summary>
public sealed class BenchmarkService
{
    private readonly IFrameCapture _capture;
    private readonly TweakEngine _engine;
    private readonly ChangeGuard _guard;

    public BenchmarkService(IFrameCapture capture, TweakEngine engine, ChangeGuard guard)
    {
        _capture = capture;
        _engine = engine;
        _guard = guard;
    }

    public bool IsAvailable => _capture.IsAvailable;
    public static TimeSpan CaptureDuration { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Compares the game with and without the tweak. If the tweak is already on (e.g. game mode applied it),
    /// it measures "with" first, then "without". The tweak stays on only if the result is clearly better.
    /// </summary>
    public async Task<string> TestTweakAsync(int processId, string tweakId, IProgress<string>? progress, CancellationToken ct)
    {
        if (!_capture.IsAvailable)
            return "Para medir FPS é preciso o PresentMon (ferramenta gratuita da Intel). Ainda não está disponível neste PC.";

        var wasActive = _engine.IsActive(tweakId);
        FrameStats? withTweak, without;
        try
        {
            if (wasActive)
            {
                progress?.Report("A medir com o ajuste (joga normalmente)…");
                withTweak = await MeasureAsync(processId, ct).ConfigureAwait(false);
                if (withTweak is null) return NotEnoughFrames;
                progress?.Report("A desligar o ajuste…");
                await _engine.RevertAsync(tweakId, ct).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
                progress?.Report("A medir sem o ajuste…");
                without = await MeasureAsync(processId, ct).ConfigureAwait(false);
            }
            else
            {
                progress?.Report("A medir sem o ajuste (joga normalmente)…");
                without = await MeasureAsync(processId, ct).ConfigureAwait(false);
                if (without is null) return NotEnoughFrames;
                await _guard.EnsureRestorePointAsync(ct).ConfigureAwait(false);
                progress?.Report("A aplicar o ajuste…");
                var applied = await _engine.ApplyAsync(tweakId, ct).ConfigureAwait(false);
                if (applied.Action != HistoryAction.Applied)
                    return OptimizerService.Describe(applied) ?? "Não foi possível aplicar.";
                await Task.Delay(TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
                progress?.Report("A medir com o ajuste…");
                withTweak = await MeasureAsync(processId, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            await RestoreAsync(tweakId, wasActive).ConfigureAwait(false);
            throw;
        }

        if (withTweak is null || without is null)
        {
            await RestoreAsync(tweakId, wasActive).ConfigureAwait(false);
            return NotEnoughFrames;
        }

        var cmp = BenchmarkComparison.Compare(without, withTweak);
        var keep = cmp.Verdict == BenchmarkVerdict.Better;
        if (keep && !_engine.IsActive(tweakId)) await _engine.ApplyAsync(tweakId, CancellationToken.None).ConfigureAwait(false);
        if (!keep && _engine.IsActive(tweakId)) await _engine.RevertAsync(tweakId, CancellationToken.None).ConfigureAwait(false);
        return cmp.Summary;
    }

    private const string NotEnoughFrames = "Não foram captadas imagens suficientes. O jogo está aberto e em primeiro plano? Nada ficou alterado.";

    private async Task<FrameStats?> MeasureAsync(int pid, CancellationToken ct) =>
        FrameStats.FromFrameTimes(await _capture.CaptureAsync(pid, CaptureDuration, ct).ConfigureAwait(false));

    /// <summary>Puts the tweak back to how it was before the test.</summary>
    private async Task RestoreAsync(string tweakId, bool wasActive)
    {
        try
        {
            if (wasActive && !_engine.IsActive(tweakId)) await _engine.ApplyAsync(tweakId, CancellationToken.None).ConfigureAwait(false);
            if (!wasActive && _engine.IsActive(tweakId)) await _engine.RevertAsync(tweakId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) { AppLog.Error("Falha ao repor ajuste após teste", ex); }
    }
}
