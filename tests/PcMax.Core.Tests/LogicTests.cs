using PcMax.Core.Alerts;
using PcMax.Core.Benchmark;
using PcMax.Core.Doctor;
using PcMax.Core.Network;
using PcMax.Core.Sensors;
using Xunit;

namespace PcMax.Core.Tests;

public class LogicTests
{
    [Fact]
    public void Doctor_flags_60hz_on_144hz_monitor_first()
    {
        var p = new HardwareProfile { Monitors = new[] { new MonitorInfo("LG", 60, 144, 1920, 1080, true) } };
        var f = DoctorEngine.Analyze(p);
        Assert.Equal(FindingSeverity.Critical, f[0].Severity);
        Assert.Equal("display.max-refresh", f[0].Fix!.TweakId);
    }

    [Fact]
    public void Doctor_flags_xmp_off()
    {
        var p = new HardwareProfile { MemoryModules = new[] { new MemoryModule("DIMM_A2", 8, 2133, 3200), new MemoryModule("DIMM_B2", 8, 2133, 3200) } };
        Assert.Contains(DoctorEngine.Analyze(p), x => x.Id == "ram-xmp");
    }

    [Theory]
    [InlineData("DIMM_A1", 'A')]
    [InlineData("ChannelB-DIMM0", 'B')]
    [InlineData("A2", 'A')]
    [InlineData("BANK 0", null)]
    public void Channel_parsing(string slot, char? expected) => Assert.Equal(expected, DoctorEngine.ChannelOf(slot));

    [Fact]
    public void Doctor_flags_two_sticks_same_channel()
    {
        var p = new HardwareProfile { MemoryModules = new[] { new MemoryModule("DIMM_A1", 8, 3200, 3200), new MemoryModule("DIMM_A2", 8, 3200, 3200) } };
        Assert.Contains(DoctorEngine.Analyze(p), x => x.Id == "ram-wrong-slots");
    }

    [Fact]
    public void Healthy_pc_scores_100()
    {
        var p = new HardwareProfile { Monitors = new[] { new MonitorInfo("X", 144, 144, 1920, 1080, true) } };
        Assert.Equal(100, DoctorEngine.HealthScore(DoctorEngine.Analyze(p)));
    }

    [Fact]
    public void FrameStats_one_percent_low()
    {
        var ft = Enumerable.Repeat(10.0, 990).Concat(Enumerable.Repeat(50.0, 10)).ToList();
        var s = FrameStats.FromFrameTimes(ft)!;
        Assert.Equal(20.0, s.OnePercentLowFps, 3);
        Assert.True(s.AverageFps > 95 && s.AverageFps < 97);
    }

    [Fact]
    public void Small_change_is_noise()
    {
        var a = FrameStats.FromFrameTimes(Enumerable.Repeat(10.0, 500))!;
        var b = FrameStats.FromFrameTimes(Enumerable.Repeat(9.9, 500))!;
        Assert.Equal(BenchmarkVerdict.NoChange, BenchmarkComparison.Compare(a, b).Verdict);
    }

    [Fact]
    public void Network_stats_loss_and_jitter()
    {
        var s = NetworkStats.FromSamples("h", new double?[] { 10, 20, null, 10 });
        Assert.Equal(25, s.LossPercent);
        Assert.Equal(10, s.JitterMs);
        Assert.Equal(ConnectionQuality.Bad, s.Quality);
    }

    [Fact]
    public void Bufferbloat_grade()
    {
        var idle = NetworkStats.FromSamples("h", new double?[] { 10, 10 });
        var load = NetworkStats.FromSamples("h", new double?[] { 110, 110 });
        Assert.Equal("C", Bufferbloat.Grade(idle, load).Grade);
    }

    [Fact]
    public void Alert_needs_sustained_heat_and_respects_cooldown()
    {
        var eng = new AlertEngine(new TemperatureThresholds { SustainSeconds = 10 });
        var t0 = DateTimeOffset.UtcNow;
        Assert.Empty(eng.Evaluate(new SensorSnapshot { Timestamp = t0, CpuTemperatureC = 90 }));
        var fired = eng.Evaluate(new SensorSnapshot { Timestamp = t0.AddSeconds(11), CpuTemperatureC = 90 });
        Assert.Single(fired);
        Assert.Equal(AlertSeverity.Warning, fired[0].Severity);
        Assert.Empty(eng.Evaluate(new SensorSnapshot { Timestamp = t0.AddSeconds(20), CpuTemperatureC = 90 }));
        // escalation to critical fires despite cooldown
        var crit = eng.Evaluate(new SensorSnapshot { Timestamp = t0.AddSeconds(25), CpuTemperatureC = 97 });
        Assert.Equal(AlertSeverity.Critical, Assert.Single(crit).Severity);
    }

    [Fact]
    public void Thresholds_are_sanitized()
    {
        var t = new TemperatureThresholds { CpuWarning = 500, CpuCritical = 10 }.Normalize();
        Assert.True(t.CpuCritical > t.CpuWarning);
        Assert.InRange(t.CpuWarning, 30, 110);
    }

    [Fact]
    public void Protected_processes_are_removed_from_settings()
    {
        var s = new Settings.AppSettings { BackgroundAppsToSuspend = new[] { "explorer", "Spotify", "lsass.exe", "vgc" } }.Normalize();
        Assert.Equal(new[] { "Spotify" }, s.BackgroundAppsToSuspend);
    }
}
