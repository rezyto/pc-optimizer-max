using PcMax.Core.Doctor;
using PcMax.Core.Settings;
using PcMax.Core.Tweaks;
using Xunit;

namespace PcMax.Core.Tests;

public class RobustnessTests
{
    private sealed class SimpleTweak : ITweak
    {
        public string Value = "original";
        public CancellationToken SeenToken;
        public Func<Task>? DuringApply;
        public string Id { get; init; } = "t";
        public string Name => Id;
        public string Description => "";
        public RiskLevel Risk => RiskLevel.Safe;
        public TweakCategory Category => TweakCategory.Power;
        public bool GameSessionOnly { get; init; }
        public Task<bool> IsSupportedAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> IsAppliedAsync(CancellationToken ct = default) => Task.FromResult(Value == "tweaked");
        public async Task<TweakSnapshot> ApplyAsync(CancellationToken ct = default)
        {
            SeenToken = ct;
            if (DuringApply is not null) await DuringApply();
            var s = TweakSnapshot.Create(Id, new Dictionary<string, string?> { ["v"] = Value });
            Value = "tweaked";
            return s;
        }
        public Task RevertAsync(TweakSnapshot s, CancellationToken ct = default)
        {
            Value = s.PreviousValues["v"]!;
            return Task.CompletedTask;
        }
    }

    /// <summary>Saves succeed until <see cref="FailSaves"/> is set.</summary>
    private sealed class FlakyStore : ISnapshotStore
    {
        public bool FailSaves;
        private List<TweakSnapshot> _items = new();
        public IReadOnlyList<TweakSnapshot> LoadActive() => _items.ToList();
        public void SaveActive(IReadOnlyList<TweakSnapshot> snapshots)
        {
            if (FailSaves) throw new IOException("disk busy");
            _items = snapshots.ToList();
        }
    }

    // ------------------------------------------------------------------ TweakEngine

    [Fact]
    public async Task Started_apply_is_never_cancelled_half_way()
    {
        var t = new SimpleTweak();
        var e = new TweakEngine(new[] { t }, new InMemorySnapshotStore());
        using var cts = new CancellationTokenSource();
        t.DuringApply = () => { cts.Cancel(); return Task.CompletedTask; };
        var r = await e.ApplyAsync(t.Id, cts.Token);
        Assert.False(t.SeenToken.CanBeCanceled);
        Assert.Equal(HistoryAction.Applied, r.Action);
        Assert.True(e.IsActive(t.Id)); // the change is tracked, so it can be undone
    }

    [Fact]
    public async Task Revert_all_keeps_going_when_saving_fails()
    {
        var a = new SimpleTweak { Id = "a" };
        var b = new SimpleTweak { Id = "b" };
        var store = new FlakyStore();
        var e = new TweakEngine(new ITweak[] { a, b }, store);
        Exception? reported = null;
        e.PersistFailed += (_, ex) => reported = ex;
        await e.ApplyAsync("a");
        await e.ApplyAsync("b");

        store.FailSaves = true;
        var results = await e.RevertAllAsync();

        Assert.All(results, r => Assert.Equal(HistoryAction.Reverted, r.Action));
        Assert.Equal("original", a.Value);
        Assert.Equal("original", b.Value);
        Assert.Empty(e.ActiveSnapshots);
        Assert.IsType<IOException>(reported);
    }

    [Fact]
    public async Task State_can_be_read_while_other_threads_apply_and_revert()
    {
        var tweaks = Enumerable.Range(0, 8).Select(i => new SimpleTweak { Id = "t" + i }).ToArray();
        var e = new TweakEngine(tweaks, new InMemorySnapshotStore());
        using var stop = new CancellationTokenSource();
        var reader = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                _ = e.IsActive("t3");
                _ = e.ActiveSnapshots.Count;
                _ = e.History.Count;
            }
        });
        for (var round = 0; round < 200; round++)
        {
            foreach (var t in tweaks) await e.ApplyAsync(t.Id);
            await e.RevertAllAsync();
        }
        stop.Cancel();
        await reader; // throws if a reader ever saw a list being modified
    }

    // ------------------------------------------------------------------ Doctor: RAM speed

    private static HardwareProfile Ram(int configured, int rated, bool laptop = false) => new()
    {
        IsLaptop = laptop,
        MemoryModules = new[] { new MemoryModule("DIMM_A2", 8, configured, rated), new MemoryModule("DIMM_B2", 8, configured, rated) },
    };

    [Theory]
    [InlineData(1333, 2666)] // pre-SMBIOS 3.1.1: clock in MHz next to a data rate in MT/s = running at rated speed
    [InlineData(1600, 3200)]
    [InlineData(2400, 4800)]
    public void Ram_reported_in_mhz_is_not_flagged(int configured, int rated) =>
        Assert.DoesNotContain(DoctorEngine.Analyze(Ram(configured, rated)), f => f.Id == "ram-xmp");

    [Fact]
    public void Ram_on_laptops_is_not_flagged() =>
        Assert.DoesNotContain(DoctorEngine.Analyze(Ram(2133, 3200, laptop: true)), f => f.Id == "ram-xmp");

    [Fact]
    public void Ram_with_xmp_on_is_not_flagged() =>
        Assert.DoesNotContain(DoctorEngine.Analyze(Ram(6000, 4800)), f => f.Id == "ram-xmp");

    [Fact]
    public void Ram_at_a_non_jedec_speed_is_not_flagged() =>
        Assert.DoesNotContain(DoctorEngine.Analyze(Ram(3000, 3600)), f => f.Id == "ram-xmp");

    [Fact]
    public void Ram_at_jedec_default_below_rated_is_a_problem_not_critical()
    {
        var f = Assert.Single(DoctorEngine.Analyze(Ram(2133, 3200)), x => x.Id == "ram-xmp");
        Assert.Equal(FindingSeverity.Problem, f.Severity);
        Assert.Contains("2133 MT/s", f.Title);
    }

    // ------------------------------------------------------------------ settings / files

    [Fact]
    public void Equal_settings_compare_equal_even_with_new_lists()
    {
        var a = new AppSettings().Normalize();
        var b = new AppSettings().Normalize();
        Assert.False(a == b); // record equality compares list references
        Assert.True(a.SameAs(b));
        Assert.False(a.SameAs(a with { AutoGameMode = !a.AutoGameMode }));
        Assert.False(a.SameAs(a with { BackgroundAppsToSuspend = new[] { "Discord" } }));
    }

    [Fact]
    public void Atomic_write_replaces_and_leaves_no_temp_files()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pcmax-atomic-" + Guid.NewGuid().ToString("N"));
        try
        {
            var f = Path.Combine(dir, "state.json");
            AtomicFile.WriteAllText(f, "one");
            AtomicFile.WriteAllText(f, "two");
            Assert.Equal("two", File.ReadAllText(f));
            Assert.Equal(new[] { "state.json" }, Directory.GetFiles(dir).Select(Path.GetFileName).ToArray());
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
