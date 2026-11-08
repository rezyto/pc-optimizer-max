using System.Runtime.Versioning;
using PcMax.Core.Settings;
using PcMax.Core.Tweaks;
using PcMax.Windows.Hardware;

namespace PcMax.Windows.Tweaks;

/// <summary>
/// While a game runs, switch to the Ultimate Performance scheme (desktops) or High Performance
/// (laptops, where Ultimate only wastes battery/heat). Restores the previous scheme afterwards.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class PowerPlanTweak : ITweak
{
    private const string PreviousKey = "previousScheme";
    private const string TargetKey = "targetScheme";
    private const string DuplicatedKey = "duplicatedScheme";

    private readonly AppPaths _paths;
    public PowerPlanTweak(AppPaths paths) => _paths = paths;

    public string Id => "power.high-performance";
    public string Name => "Plano de energia de alto desempenho durante o jogo";
    public string Description =>
        "Enquanto jogas, muda o plano de energia do Windows para \"Desempenho Máximo\" (ou \"Alto Desempenho\" nos portáteis), " +
        "para o processador não baixar a velocidade a meio do jogo. Quando fechas o jogo, volta ao plano que tinhas antes. " +
        "Se for preciso, cria uma cópia do plano \"Desempenho Máximo\" (é reutilizada nas próximas vezes).";
    public RiskLevel Risk => RiskLevel.Safe;
    public TweakCategory Category => TweakCategory.Power;
    public bool GameSessionOnly => true;

    public Task<bool> IsSupportedAsync(CancellationToken ct = default)
    {
        try
        {
            // Supported if we can read the active scheme; a target is found or created on apply.
            return Task.FromResult(PowerPlans.GetActive() is not null);
        }
        catch (Exception ex)
        {
            Log.Warn("power.high-performance: sem acesso aos planos de energia", ex);
            return Task.FromResult(false);
        }
    }

    public Task<bool> IsAppliedAsync(CancellationToken ct = default)
    {
        try { return Task.FromResult(PowerPlans.GetActive() is { } g && PowerPlans.IsHighPerformance(g, _paths)); }
        catch { return Task.FromResult(false); }
    }

    public Task<TweakSnapshot> ApplyAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        var previous = PowerPlans.GetActive() ?? throw new InvalidOperationException("Não foi possível ler o plano de energia ativo.");
        var values = new Dictionary<string, string?> { [PreviousKey] = previous.ToString("D") };

        if (PowerPlans.IsHighPerformance(previous, _paths))
        {
            // Already fast: record a no-op so the revert does nothing.
            values[TargetKey] = previous.ToString("D");
            return TweakSnapshot.Create(Id, values);
        }

        var (target, duplicated) = FindOrCreateTarget();
        if (target is not { } t) throw new InvalidOperationException("Não existe um plano de alto desempenho neste PC e não foi possível criá-lo.");
        PowerPlans.SetActive(t);
        values[TargetKey] = t.ToString("D");
        values[DuplicatedKey] = duplicated?.ToString("D");
        Log.Info($"Plano de energia: {previous} -> {t}");
        return TweakSnapshot.Create(Id, values);
    }, ct);

    public Task RevertAsync(TweakSnapshot snapshot, CancellationToken ct = default) => Task.Run(() =>
    {
        if (!snapshot.PreviousValues.TryGetValue(PreviousKey, out var prevText) || !Guid.TryParse(prevText, out var previous))
            throw new InvalidOperationException("Snapshot do plano de energia inválido.");
        Guid.TryParse(snapshot.PreviousValues.GetValueOrDefault(TargetKey), out var target);

        var active = PowerPlans.GetActive();
        if (active == previous) return;
        // If the user picked another plan by hand during the game, respect that choice.
        if (target != Guid.Empty && active is { } a && a != target)
        {
            Log.Info($"Plano de energia mudado pelo utilizador ({a}); não foi reposto.");
            return;
        }
        var restore = PowerPlans.Exists(previous) ? previous : PowerPlans.Balanced;
        PowerPlans.SetActive(restore);
        Log.Info($"Plano de energia reposto: {restore}");
    }, ct);

    /// <summary>Only our three keys; "previousScheme" must be a GUID, the others a GUID or null.</summary>
    public bool AcceptsSnapshot(TweakSnapshot snapshot)
    {
        if (snapshot.TweakId != Id || snapshot.PreviousValues is not { Count: > 0 and <= 3 } values) return false;
        foreach (var (key, value) in values)
        {
            switch (key)
            {
                case PreviousKey:
                    if (!IsGuid(value)) return false;
                    break;
                case TargetKey:
                case DuplicatedKey:
                    if (value is not null && !IsGuid(value)) return false;
                    break;
                default:
                    return false;
            }
        }
        return values.ContainsKey(PreviousKey);
    }

    private static bool IsGuid(string? s) => s is { Length: 36 } && Guid.TryParseExact(s, "D", out var g) && g != Guid.Empty;

    private (Guid? Target, Guid? Duplicated) FindOrCreateTarget()
    {
        var schemes = PowerPlans.List();
        var remembered = PowerPlans.LoadDuplicated(_paths);
        var laptop = HardwareInspector.IsLaptopCached();

        if (!laptop && schemes.Contains(PowerPlans.UltimatePerformance)) return (PowerPlans.UltimatePerformance, null);
        if (laptop && schemes.Contains(PowerPlans.HighPerformance)) return (PowerPlans.HighPerformance, null);
        if (remembered is { } r && schemes.Contains(r)) return (r, null);

        var source = laptop ? PowerPlans.HighPerformance : PowerPlans.UltimatePerformance;
        if (PowerPlans.Duplicate(source) is { } dup)
        {
            PowerPlans.SaveDuplicated(_paths, dup);
            return (dup, dup);
        }
        if (schemes.Contains(PowerPlans.HighPerformance)) return (PowerPlans.HighPerformance, null);
        if (!laptop && PowerPlans.Duplicate(PowerPlans.HighPerformance) is { } dup2)
        {
            PowerPlans.SaveDuplicated(_paths, dup2);
            return (dup2, dup2);
        }
        return (null, null);
    }
}
