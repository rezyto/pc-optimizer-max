using PcMax.Core.Tweaks;
using Xunit;

namespace PcMax.Core.Tests;

public class TweakEngineTests
{
    private sealed class FakeTweak : ITweak
    {
        public string Value = "original";
        public bool FailApply;
        public string Id { get; init; } = "fake";
        public string Name => "Fake";
        public string Description => "";
        public RiskLevel Risk { get; init; } = RiskLevel.Safe;
        public TweakCategory Category => TweakCategory.Power;
        public bool GameSessionOnly { get; init; }
        public Task<bool> IsSupportedAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> IsAppliedAsync(CancellationToken ct = default) => Task.FromResult(Value == "tweaked");
        public Task<TweakSnapshot> ApplyAsync(CancellationToken ct = default)
        {
            if (FailApply) throw new InvalidOperationException("boom");
            var snap = TweakSnapshot.Create(Id, new Dictionary<string, string?> { ["v"] = Value });
            Value = "tweaked";
            return Task.FromResult(snap);
        }
        public Task RevertAsync(TweakSnapshot s, CancellationToken ct = default)
        {
            Value = s.PreviousValues["v"]!;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Apply_then_revert_restores_original()
    {
        var t = new FakeTweak();
        var e = new TweakEngine(new[] { t }, new InMemorySnapshotStore());
        Assert.Equal(HistoryAction.Applied, (await e.ApplyAsync("fake")).Action);
        Assert.Equal("tweaked", t.Value);
        await e.RevertAsync("fake");
        Assert.Equal("original", t.Value);
        Assert.False(e.IsActive("fake"));
    }

    [Fact]
    public async Task Applying_twice_keeps_first_snapshot()
    {
        var t = new FakeTweak();
        var e = new TweakEngine(new[] { t }, new InMemorySnapshotStore());
        await e.ApplyAsync("fake");
        await e.ApplyAsync("fake");
        Assert.Single(e.ActiveSnapshots);
        await e.RevertAsync("fake");
        Assert.Equal("original", t.Value);
    }

    [Fact]
    public async Task Risk_above_limit_is_refused()
    {
        var t = new FakeTweak { Risk = RiskLevel.Advanced };
        var e = new TweakEngine(new[] { t }, new InMemorySnapshotStore());
        var r = await e.ApplyAsync("fake");
        Assert.Equal(HistoryAction.SkippedRisk, r.Action);
        Assert.Equal("original", t.Value);
    }

    [Fact]
    public async Task Failed_apply_is_recorded_and_not_active()
    {
        var t = new FakeTweak { FailApply = true };
        var e = new TweakEngine(new[] { t }, new InMemorySnapshotStore());
        var r = await e.ApplyAsync("fake");
        Assert.Equal(HistoryAction.Failed, r.Action);
        Assert.False(e.IsActive("fake"));
    }

    [Fact]
    public async Task Snapshots_survive_restart_and_can_be_reverted()
    {
        var store = new InMemorySnapshotStore();
        var t = new FakeTweak();
        await new TweakEngine(new[] { t }, store).ApplyAsync("fake");
        // "crash": new engine instance reads the store
        var e2 = new TweakEngine(new[] { t }, store);
        Assert.True(e2.IsActive("fake"));
        await e2.RevertAllAsync();
        Assert.Equal("original", t.Value);
    }

    [Fact]
    public async Task RevertGameSession_only_reverts_session_tweaks()
    {
        var a = new FakeTweak { Id = "a", GameSessionOnly = true };
        var b = new FakeTweak { Id = "b", GameSessionOnly = false };
        var e = new TweakEngine(new ITweak[] { a, b }, new InMemorySnapshotStore());
        await e.ApplyAsync("a");
        await e.ApplyAsync("b");
        await e.RevertGameSessionAsync();
        Assert.Equal("original", a.Value);
        Assert.Equal("tweaked", b.Value);
    }
}
