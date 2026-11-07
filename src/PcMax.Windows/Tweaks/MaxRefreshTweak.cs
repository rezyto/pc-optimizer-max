using System.Globalization;
using System.Runtime.Versioning;
using PcMax.Core.Tweaks;
using PcMax.Windows.Hardware;

namespace PcMax.Windows.Tweaks;

/// <summary>
/// Sets every attached monitor to the highest refresh rate it offers at its current resolution.
/// Snapshot: one entry per GDI device name -> "hz|width|height" before the change.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class MaxRefreshTweak : ITweak
{
    public string Id => "display.max-refresh";
    public string Name => "Usar a frequência máxima do monitor";
    public string Description =>
        "Muitos monitores de 120, 144 ou 165 Hz vêm configurados a 60 Hz no Windows. Isto põe cada ecrã na frequência mais " +
        "alta que ele suporta na resolução atual, para veres mais imagens por segundo. A resolução não muda. " +
        "Se quiseres, podes voltar à frequência anterior.";
    public RiskLevel Risk => RiskLevel.Safe;
    public TweakCategory Category => TweakCategory.Display;
    public bool GameSessionOnly => false;

    public Task<bool> IsSupportedAsync(CancellationToken ct = default)
    {
        try { return Task.FromResult(Displays.List().Count > 0); }
        catch { return Task.FromResult(false); }
    }

    public Task<bool> IsAppliedAsync(CancellationToken ct = default)
    {
        try { return Task.FromResult(Displays.List().All(d => d.CurrentHz >= d.MaxHz)); }
        catch { return Task.FromResult(false); }
    }

    public Task<TweakSnapshot> ApplyAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        var previous = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var failures = new List<string>();
        foreach (var d in Displays.List())
        {
            ct.ThrowIfCancellationRequested();
            if (d.MaxHz <= d.CurrentHz) continue;
            previous[d.DeviceName] = string.Create(CultureInfo.InvariantCulture, $"{d.CurrentHz}|{d.Width}|{d.Height}");
            if (!Displays.TrySetRefreshRate(d.DeviceName, d.MaxHz))
            {
                previous.Remove(d.DeviceName);
                failures.Add(d.FriendlyName);
            }
        }
        if (previous.Count == 0 && failures.Count > 0)
            throw new InvalidOperationException("O Windows recusou a nova frequência em: " + string.Join(", ", failures));
        return TweakSnapshot.Create(Id, previous);
    }, ct);

    public Task RevertAsync(TweakSnapshot snapshot, CancellationToken ct = default) => Task.Run(() =>
    {
        var current = Displays.List().ToDictionary(d => d.DeviceName, StringComparer.OrdinalIgnoreCase);
        var failures = new List<string>();
        foreach (var (device, value) in snapshot.PreviousValues)
        {
            if (value is null) continue;
            var parts = value.Split('|');
            if (parts.Length != 3 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hz)
                || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var w)
                || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var h))
                continue;
            if (!current.TryGetValue(device, out var d))
            {
                Log.Warn($"display.max-refresh: {device} já não está ligado; nada a repor.");
                continue;
            }
            // Only restore when the screen is still at the same resolution and the old rate exists there.
            var sameRes = (d.Width == w && d.Height == h) || (d.Width == h && d.Height == w);
            if (!sameRes || !d.AvailableHz.Contains(hz))
            {
                Log.Warn($"display.max-refresh: {device} mudou de resolução; {hz} Hz não reposto.");
                continue;
            }
            if (!Displays.TrySetRefreshRate(device, hz)) failures.Add(d.FriendlyName);
        }
        if (failures.Count > 0)
            throw new InvalidOperationException("Não foi possível repor a frequência em: " + string.Join(", ", failures));
    }, ct);
}
