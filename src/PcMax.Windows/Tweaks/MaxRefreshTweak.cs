using System.Globalization;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using PcMax.Core.Tweaks;
using PcMax.Windows.Hardware;

namespace PcMax.Windows.Tweaks;

/// <summary>
/// Sets every attached monitor to the highest refresh rate it offers at its current resolution.
/// Snapshot: one entry per GDI device name -> "hz|width|height|monitorId" before the change. The monitor id
/// (device interface path) is what the revert matches on, so a renumbered \\.\DISPLAYn never gets another
/// monitor's old rate. Old snapshots without the id fall back to the GDI name.
/// Applying is all-or-nothing per call: if a later display throws, the ones already changed are put back.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed partial class MaxRefreshTweak : ITweak
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
        // No cancellation once the first display changed: a partial change must always end up in the snapshot.
        ct.ThrowIfCancellationRequested();
        var previous = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var changed = new List<DisplayState>();
        var failures = new List<string>();
        try
        {
            foreach (var d in Displays.List())
            {
                if (d.MaxHz <= d.CurrentHz) continue;
                if (!Displays.TrySetRefreshRate(d.DeviceName, d.MaxHz))
                {
                    failures.Add(d.FriendlyName);
                    continue;
                }
                changed.Add(d);
                previous[d.DeviceName] = Encode(d.CurrentHz, d.Width, d.Height, d.MonitorId);
            }
        }
        catch
        {
            foreach (var d in changed)
            {
                try { Displays.TrySetRefreshRate(d.DeviceName, d.CurrentHz); } catch { }
            }
            throw;
        }
        if (previous.Count == 0 && failures.Count > 0)
            throw new InvalidOperationException("O Windows recusou a nova frequência em: " + string.Join(", ", failures));
        return TweakSnapshot.Create(Id, previous);
    }, CancellationToken.None);

    private static string Encode(int hz, int w, int h, string? monitorId) => monitorId is null
        ? string.Create(CultureInfo.InvariantCulture, $"{hz}|{w}|{h}")
        : string.Create(CultureInfo.InvariantCulture, $"{hz}|{w}|{h}|{monitorId}");

    /// <summary>
    /// Keys must be GDI display names ("\\.\DISPLAY1"), values "hz|width|height[|monitorId]" with sane numbers (or null).
    /// </summary>
    public bool AcceptsSnapshot(TweakSnapshot snapshot)
    {
        if (snapshot.TweakId != Id || snapshot.PreviousValues is not { Count: <= 16 } values) return false;
        foreach (var (device, value) in values)
        {
            if (!DeviceRegex().IsMatch(device)) return false;
            if (value is null) continue;
            var m = EntryRegex().Match(value);
            if (!m.Success) return false;
            var hz = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var w = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            var h = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            if (hz is < 1 or > 1000 || w is < 1 or > 16384 || h is < 1 or > 16384) return false;
        }
        return true;
    }

    [GeneratedRegex(@"^\\\\\.\\DISPLAY[0-9]{1,3}$", RegexOptions.CultureInvariant)]
    private static partial Regex DeviceRegex();

    // Monitor id: a device interface path; printable, no '|', capped.
    [GeneratedRegex(@"^([0-9]{1,4})\|([0-9]{1,5})\|([0-9]{1,5})(?:\|([\x21-\x7B\x7D\x7E]{1,256}))?$", RegexOptions.CultureInvariant)]
    private static partial Regex EntryRegex();

    public Task RevertAsync(TweakSnapshot snapshot, CancellationToken ct = default) => Task.Run(() =>
    {
        var displays = Displays.List();
        var current = displays.ToDictionary(d => d.DeviceName, StringComparer.OrdinalIgnoreCase);
        var failures = new List<string>();
        foreach (var (device, value) in snapshot.PreviousValues)
        {
            if (value is null) continue;
            var parts = value.Split('|', 4);
            if (parts.Length is not (3 or 4) || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hz)
                || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var w)
                || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var h))
                continue;
            DisplayState? d;
            if (parts.Length == 4)
            {
                // Match the monitor itself, wherever its GDI number is now.
                d = displays.FirstOrDefault(x => string.Equals(x.MonitorId, parts[3], StringComparison.OrdinalIgnoreCase));
                if (d is null)
                {
                    Log.Warn($"display.max-refresh: o monitor de {device} já não está ligado; nada a repor.");
                    continue;
                }
            }
            else if (!current.TryGetValue(device, out d))
            {
                Log.Warn($"display.max-refresh: {device} já não está ligado; nada a repor.");
                continue;
            }
            // Only restore when the screen is still at the same resolution and the old rate exists there.
            var sameRes = (d.Width == w && d.Height == h) || (d.Width == h && d.Height == w);
            if (!sameRes || !d.AvailableHz.Contains(hz))
            {
                Log.Warn($"display.max-refresh: {d.DeviceName} mudou de resolução; {hz} Hz não reposto.");
                continue;
            }
            if (!Displays.TrySetRefreshRate(d.DeviceName, hz)) failures.Add(d.FriendlyName);
        }
        if (failures.Count > 0)
            throw new InvalidOperationException("Não foi possível repor a frequência em: " + string.Join(", ", failures));
    }, ct);
}
