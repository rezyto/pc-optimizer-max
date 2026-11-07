using PcMax.Core.Sensors;

namespace PcMax.Core.Alerts;

/// <summary>
/// Turns sensor readings into alerts. A value must stay above its limit for
/// <see cref="TemperatureThresholds.SustainSeconds"/> before an alert fires, the same alert
/// does not repeat within the cooldown, and it re-arms only after dropping a few degrees below
/// the limit (hysteresis), so the user is not spammed.
/// </summary>
public sealed class AlertEngine
{
    private sealed class State
    {
        public DateTimeOffset? AboveSince;
        public AlertSeverity? Fired;
        public DateTimeOffset LastFired = DateTimeOffset.MinValue;
    }

    private readonly Dictionary<string, State> _states = new();
    public TemperatureThresholds Thresholds { get; set; }
    public TimeSpan Cooldown { get; set; } = TimeSpan.FromMinutes(5);
    public double HysteresisC { get; set; } = 3;

    public AlertEngine(TemperatureThresholds thresholds) => Thresholds = thresholds.Normalize();

    public IReadOnlyList<Alert> Evaluate(SensorSnapshot s)
    {
        var t = Thresholds;
        var list = new List<Alert>();
        Check(list, "cpu-temp", "CPU", s.CpuTemperatureC, t.CpuWarning, t.CpuCritical, s.Timestamp);
        Check(list, "gpu-temp", "Placa gráfica", s.GpuTemperatureC, t.GpuWarning, t.GpuCritical, s.Timestamp);
        Check(list, "storage-temp", "Disco", s.StorageTemperatureC, t.StorageWarning, t.StorageCritical, s.Timestamp);

        if (s.CpuThrottling == true)
            Simple(list, "cpu-throttle", AlertSeverity.Warning, "O CPU está a abrandar",
                "O processador baixou a velocidade para se proteger (calor ou limite de energia). Isto causa quebras de FPS.", s.Timestamp);
        if (s.RamUsedPercent is >= 92)
            Simple(list, "ram-full", AlertSeverity.Warning, "Memória quase cheia",
                $"A RAM está a {s.RamUsedPercent:0}%. Fecha programas que não precisas para evitar travões.", s.Timestamp);
        return list;
    }

    private void Check(List<Alert> list, string key, string label, double? value, double warn, double crit, DateTimeOffset now)
    {
        if (!_states.TryGetValue(key, out var st)) _states[key] = st = new State();
        if (value is null) { st.AboveSince = null; return; }
        var v = value.Value;

        if (v < warn - HysteresisC)
        {
            st.AboveSince = null;
            st.Fired = null;
            return;
        }
        if (v < warn)
        {
            st.AboveSince = null;
            return;
        }

        st.AboveSince ??= now;
        if (now - st.AboveSince.Value < TimeSpan.FromSeconds(Thresholds.SustainSeconds)) return;

        var sev = v >= crit ? AlertSeverity.Critical : AlertSeverity.Warning;
        var escalated = st.Fired is null || sev > st.Fired;
        if (!escalated && now - st.LastFired < Cooldown) return;

        st.Fired = sev;
        st.LastFired = now;
        var title = sev == AlertSeverity.Critical ? $"{label} demasiado quente!" : $"{label} a aquecer";
        var msg = sev == AlertSeverity.Critical
            ? $"{label} está a {v:0} °C (limite crítico {crit:0} °C). Guarda o jogo e verifica a ventilação. Os ajustes de desempenho foram pausados."
            : $"{label} está a {v:0} °C (aviso a partir de {warn:0} °C). Verifica ventoinhas e pó.";
        list.Add(new Alert(key, sev, title, msg, now));
    }

    private void Simple(List<Alert> list, string key, AlertSeverity sev, string title, string msg, DateTimeOffset now)
    {
        if (!_states.TryGetValue(key, out var st)) _states[key] = st = new State();
        if (now - st.LastFired < Cooldown) return;
        st.LastFired = now;
        list.Add(new Alert(key, sev, title, msg, now));
    }
}
