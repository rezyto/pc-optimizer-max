using PcMax.Core.Doctor;
using PcMax.Core.Platform;

namespace PcMax.App.Services;

/// <summary>Runs the PC Doctor and keeps the last result, shared by the Painel and the Médico pages.</summary>
public sealed class HealthService
{
    private readonly IHardwareInspector _inspector;
    private readonly Func<(double? cpu, double? gpu)> _recentMaxTemps;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public HealthService(IHardwareInspector inspector, Func<(double? cpu, double? gpu)> recentMaxTemps)
    {
        _inspector = inspector;
        _recentMaxTemps = recentMaxTemps;
    }

    public IReadOnlyList<Finding>? LastFindings { get; private set; }
    public HardwareProfile? LastProfile { get; private set; }
    public int? Score => LastFindings is { } f ? DoctorEngine.HealthScore(f) : null;

    /// <summary>Raised (on a background thread) after every analysis.</summary>
    public event EventHandler? Analyzed;

    public async Task<IReadOnlyList<Finding>> AnalyzeAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var profile = await _inspector.InspectAsync(ct).ConfigureAwait(false);
            // The inspector sees a single moment; the app has been watching temperatures for longer.
            var (cpu, gpu) = _recentMaxTemps();
            profile = profile with
            {
                MaxRecentCpuTempC = Max(profile.MaxRecentCpuTempC, cpu),
                MaxRecentGpuTempC = Max(profile.MaxRecentGpuTempC, gpu),
            };
            var findings = DoctorEngine.Analyze(profile);
            LastProfile = profile;
            LastFindings = findings;
            Analyzed?.Invoke(this, EventArgs.Empty);
            return findings;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static double? Max(double? a, double? b) => a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);
}
