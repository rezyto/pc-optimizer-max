using System.Runtime.Versioning;
using Microsoft.Win32;
using PcMax.Core.Tweaks;

namespace PcMax.Windows.Tweaks;

/// <summary>One DWORD registry value a tweak sets.</summary>
[SupportedOSPlatform("windows")]
internal sealed record RegistryDword(RegistryHive Hive, string SubKey, string Name, int Value)
{
    public string SnapshotKey => RegistryValue.Key(Hive, SubKey, Name);
}

/// <summary>
/// Base for tweaks that only set a fixed list of DWORD values. Captures every value (including
/// "absent") before writing; if a write fails part-way, the values already written are put back.
/// </summary>
[SupportedOSPlatform("windows")]
internal abstract class RegistryDwordTweak : ITweak
{
    protected abstract IReadOnlyList<RegistryDword> Values { get; }

    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract string Description { get; }
    public RiskLevel Risk => RiskLevel.Safe;
    public abstract TweakCategory Category { get; }
    public bool GameSessionOnly => false;

    public virtual Task<bool> IsSupportedAsync(CancellationToken ct = default) => Task.FromResult(true);

    public virtual Task<bool> IsAppliedAsync(CancellationToken ct = default) =>
        Task.FromResult(Values.All(v => RegistryValue.ReadDword(v.Hive, v.SubKey, v.Name) == v.Value));

    public Task<TweakSnapshot> ApplyAsync(CancellationToken ct = default)
    {
        var previous = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var v in Values)
            previous[v.SnapshotKey] = RegistryValue.Capture(v.Hive, v.SubKey, v.Name);

        var written = new List<RegistryDword>();
        try
        {
            foreach (var v in Values)
            {
                ct.ThrowIfCancellationRequested();
                RegistryValue.SetDword(v.Hive, v.SubKey, v.Name, v.Value);
                written.Add(v);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Tweak {Id}: falhou a escrita, a repor valores anteriores", ex);
            foreach (var v in written)
            {
                try { RegistryValue.Restore(v.Hive, v.SubKey, v.Name, previous[v.SnapshotKey]); }
                catch (Exception re) { Log.Error($"Tweak {Id}: não foi possível repor {v.SnapshotKey}", re); }
            }
            throw;
        }
        Log.Info($"Tweak {Id} aplicado.");
        return Task.FromResult(TweakSnapshot.Create(Id, previous));
    }

    public Task RevertAsync(TweakSnapshot snapshot, CancellationToken ct = default)
    {
        List<Exception>? errors = null;
        foreach (var v in Values)
        {
            // Only touch values that are in the snapshot: never write something we did not capture.
            if (!snapshot.PreviousValues.TryGetValue(v.SnapshotKey, out var captured)) continue;
            try { RegistryValue.Restore(v.Hive, v.SubKey, v.Name, captured); }
            catch (Exception ex)
            {
                Log.Error($"Tweak {Id}: falhou a reposição de {v.SnapshotKey}", ex);
                (errors ??= new()).Add(ex);
            }
        }
        if (errors is not null) throw new AggregateException("Não foi possível repor todos os valores.", errors);
        Log.Info($"Tweak {Id} revertido.");
        return Task.CompletedTask;
    }
}

[SupportedOSPlatform("windows")]
internal sealed class GameModeTweak : RegistryDwordTweak
{
    internal const string GameBarKey = @"Software\Microsoft\GameBar";

    protected override IReadOnlyList<RegistryDword> Values { get; } = new[]
    {
        new RegistryDword(RegistryHive.CurrentUser, GameBarKey, "AutoGameModeEnabled", 1),
        new RegistryDword(RegistryHive.CurrentUser, GameBarKey, "AllowAutoGameMode", 1),
    };

    public override string Id => "windows.game-mode";
    public override string Name => "Ligar o Modo de Jogo do Windows";
    public override string Description =>
        "Liga o Modo de Jogo do próprio Windows. Quando estás a jogar, o Windows dá prioridade ao jogo e evita instalar " +
        "atualizações ou mostrar notificações a meio da partida. É uma opção oficial do Windows e pode ser desligada a qualquer momento.";
    public override TweakCategory Category => TweakCategory.Background;
}

[SupportedOSPlatform("windows")]
internal sealed class BackgroundRecordingTweak : RegistryDwordTweak
{
    internal const string GameConfigStoreKey = @"System\GameConfigStore";
    internal const string GameDvrKey = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";

    protected override IReadOnlyList<RegistryDword> Values { get; } = new[]
    {
        new RegistryDword(RegistryHive.CurrentUser, GameDvrKey, "HistoricalCaptureEnabled", 0),
        new RegistryDword(RegistryHive.CurrentUser, GameDvrKey, "AppCaptureEnabled", 0),
        new RegistryDword(RegistryHive.CurrentUser, GameConfigStoreKey, "GameDVR_Enabled", 0),
    };

    public override string Id => "windows.disable-background-recording";
    public override string Name => "Desligar a gravação em segundo plano (Xbox Game Bar)";
    public override string Description =>
        "Desliga as capturas de jogo da Xbox Game Bar, incluindo a gravação contínua dos últimos minutos, que gasta " +
        "recursos da placa gráfica mesmo quando não a usas. Também deixa de funcionar a gravação manual com Win+Alt+R. " +
        "A própria Game Bar continua disponível e podes reverter isto a qualquer momento.";
    public override TweakCategory Category => TweakCategory.Gpu;
}
