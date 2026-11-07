using PcMax.Core.Doctor;
using PcMax.Core.Tweaks;

namespace PcMax.App.Services;

public sealed record OptimizeStep(string Title, bool Ok, string? Detail);

public sealed record OptimizeReport(
    int? ScoreBefore,
    int? ScoreAfter,
    IReadOnlyList<OptimizeStep> Steps,
    int ManualFindings,
    string? Warning);

/// <summary>"Otimizar agora": analyse, create a restore point, apply every automatic Safe fix, analyse again.</summary>
public sealed class OptimizerService
{
    private readonly HealthService _health;
    private readonly TweakEngine _engine;
    private readonly ChangeGuard _guard;

    public OptimizerService(HealthService health, TweakEngine engine, ChangeGuard guard)
    {
        _health = health;
        _engine = engine;
        _guard = guard;
    }

    /// <summary>Automatic fixes the app may apply right now without asking (Safe, permanent, not already on).</summary>
    public IReadOnlyList<(Finding Finding, ITweak Tweak)> AutoFixable(IEnumerable<Finding> findings) =>
        findings
            .Where(f => f.Fix?.TweakId is not null)
            .Select(f => (Finding: f, Tweak: _engine.Find(f.Fix!.TweakId!)))
            .Where(x => x.Tweak is { Risk: RiskLevel.Safe, GameSessionOnly: false } && !_engine.IsActive(x.Tweak.Id))
            .Select(x => (x.Finding, x.Tweak!))
            .ToList();

    public async Task<OptimizeReport> OptimizeAsync(IProgress<string>? progress, CancellationToken ct)
    {
        progress?.Report("A analisar o teu PC…");
        var before = await _health.AnalyzeAsync(ct).ConfigureAwait(false);
        var scoreBefore = DoctorEngine.HealthScore(before);
        var fixes = AutoFixable(before);
        var steps = new List<OptimizeStep>();
        string? warning = null;

        if (fixes.Count > 0)
        {
            progress?.Report("A criar ponto de restauro…");
            warning = await _guard.EnsureRestorePointAsync(ct).ConfigureAwait(false);

            var i = 0;
            foreach (var (finding, tweak) in fixes)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report($"A aplicar ({++i}/{fixes.Count}): {tweak.Name}…");
                var entry = await _engine.ApplyAsync(tweak.Id, ct).ConfigureAwait(false);
                steps.Add(new OptimizeStep(tweak.Name, entry.Action == HistoryAction.Applied, Describe(entry)));
            }
        }

        // Game-session tweaks are not applied now: they switch on by themselves while you play.
        foreach (var f in before.Where(f => f.Fix?.TweakId is { } id && _engine.Find(id) is { GameSessionOnly: true }))
            steps.Add(new OptimizeStep(f.Title, true, "Fica ativo automaticamente durante os jogos."));

        progress?.Report("A confirmar resultados…");
        var after = fixes.Count > 0 ? await _health.AnalyzeAsync(ct).ConfigureAwait(false) : before;
        var manual = after.Count(f => f.Fix is { IsAutomatic: false });
        return new OptimizeReport(scoreBefore, DoctorEngine.HealthScore(after), steps, manual, warning);
    }

    public static string? Describe(HistoryEntry e) => e.Action switch
    {
        HistoryAction.Applied => e.Detail ?? "Aplicado.",
        HistoryAction.Reverted => e.Detail ?? "Revertido.",
        HistoryAction.SkippedRisk => "Ignorado: acima do nível de risco escolhido.",
        HistoryAction.SkippedUnsupported => "Não suportado neste PC.",
        _ => e.Detail ?? "Falhou.",
    };
}
