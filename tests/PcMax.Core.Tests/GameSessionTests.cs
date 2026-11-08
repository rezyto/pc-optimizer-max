using PcMax.Core.Games;
using PcMax.Core.Platform;
using PcMax.Core.Settings;
using PcMax.Core.Tweaks;
using Xunit;

namespace PcMax.Core.Tests;

public class GameSessionTests
{
    /// <summary>A session tweak whose apply can be held open, to reproduce events that arrive mid-start.</summary>
    private sealed class GateTweak : ITweak
    {
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Value = "balanced";
        public bool Hold = true;
        public string Id { get; init; } = "power.high-performance";
        public string Name => Id;
        public string Description => "";
        public RiskLevel Risk => RiskLevel.Safe;
        public TweakCategory Category => TweakCategory.Power;
        public bool GameSessionOnly { get; init; } = true;
        public Task<bool> IsSupportedAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> IsAppliedAsync(CancellationToken ct = default) => Task.FromResult(Value == "high");
        public async Task<TweakSnapshot> ApplyAsync(CancellationToken ct = default)
        {
            Entered.TrySetResult();
            if (Hold) await Release.Task;
            var s = TweakSnapshot.Create(Id, new Dictionary<string, string?> { ["v"] = Value });
            Value = "high";
            return s;
        }
        public Task RevertAsync(TweakSnapshot s, CancellationToken ct = default)
        {
            Value = s.PreviousValues["v"]!;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeBooster : IProcessBooster
    {
        public readonly List<int> Suspended = new();
        public bool Running = true;
        public int? LastExclude;
        public string? BoostGame(int pid) => "Normal";
        public void RestoreGame(int pid, string? p) { }
        public IReadOnlyList<int> SuspendBackground(IEnumerable<string> names, int? excludePid = null)
        {
            LastExclude = excludePid;
            Suspended.AddRange(new[] { 111, 222 });
            return new[] { 111, 222 };
        }
        public void ResumeAll(IEnumerable<int> ids) { foreach (var i in ids.ToList()) Suspended.Remove(i); }
        public bool IsRunning(int processId, string executableName) => Running;
    }

    private static readonly GameEvent Cs2 = new("cs2.exe", 1234, "CS2");

    private static (GameSessionManager Gm, TweakEngine Engine, FakeBooster Booster) Build(params ITweak[] tweaks)
    {
        var engine = new TweakEngine(tweaks, new InMemorySnapshotStore());
        var booster = new FakeBooster();
        var settings = new AppSettings().Normalize();
        return (new GameSessionManager(engine, booster, () => settings), engine, booster);
    }

    [Fact]
    public async Task Game_stopping_while_start_applies_leaves_nothing_behind()
    {
        var t = new GateTweak();
        var (gm, engine, booster) = Build(t);

        var start = gm.OnGameStartedAsync(Cs2);
        await t.Entered.Task;
        var stop = gm.OnGameStoppedAsync(Cs2);
        t.Release.SetResult();
        await Task.WhenAll(start, stop);

        Assert.Null(gm.CurrentGame);
        Assert.Empty(booster.Suspended);
        Assert.False(engine.IsActive(t.Id));
        Assert.Equal("balanced", t.Value);
    }

    [Fact]
    public async Task After_a_stop_during_start_the_next_game_still_gets_a_session()
    {
        var t = new GateTweak();
        var (gm, engine, _) = Build(t);
        var start = gm.OnGameStartedAsync(Cs2);
        await t.Entered.Task;
        await gm.OnGameStoppedAsync(Cs2);
        t.Release.SetResult();
        await start;

        t.Hold = false;
        var next = new GameEvent("cs2.exe", 5678, "CS2");
        await gm.OnGameStartedAsync(next);
        Assert.Equal(5678, gm.CurrentGame?.ProcessId);
        Assert.True(engine.IsActive(t.Id));
        await gm.OnGameStoppedAsync(next);
        Assert.Null(gm.CurrentGame);
        Assert.False(engine.IsActive(t.Id));
    }

    [Fact]
    public async Task Abort_during_start_undoes_the_start_when_it_finishes()
    {
        var t = new GateTweak();
        var (gm, engine, booster) = Build(t);
        var start = gm.OnGameStartedAsync(Cs2);
        await t.Entered.Task;
        var abort = gm.AbortAsync();
        t.Release.SetResult();
        await Task.WhenAll(start, abort);

        Assert.Null(gm.CurrentGame);
        Assert.Empty(booster.Suspended);
        Assert.False(engine.IsActive(t.Id));
    }

    [Fact]
    public async Task Game_that_exited_before_start_finished_is_undone()
    {
        var t = new GateTweak { Hold = false };
        var (gm, engine, booster) = Build(t);
        booster.Running = false; // the stop event never comes (watcher missed it, or it fired before we listened)
        await gm.OnGameStartedAsync(Cs2);

        Assert.Null(gm.CurrentGame);
        Assert.Empty(booster.Suspended);
        Assert.False(engine.IsActive(t.Id));
    }

    [Fact]
    public async Task Failure_during_start_cleans_up_and_does_not_block_the_next_game()
    {
        var t = new GateTweak { Hold = false };
        var (gm, engine, _) = Build(t);
        var throwOnce = true;
        engine.HistoryAdded += (_, _) =>
        {
            if (!throwOnce) return;
            throwOnce = false;
            throw new InvalidOperationException("boom");
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => gm.OnGameStartedAsync(Cs2));
        Assert.Null(gm.CurrentGame);
        Assert.False(engine.IsActive(t.Id));

        await gm.OnGameStartedAsync(Cs2);
        Assert.Equal(Cs2.ProcessId, gm.CurrentGame?.ProcessId);
    }

    [Fact]
    public async Task Normal_session_suspends_with_game_excluded_and_restores_on_stop()
    {
        var t = new GateTweak { Hold = false };
        var (gm, engine, booster) = Build(t);
        await gm.OnGameStartedAsync(Cs2);
        Assert.Equal(Cs2.ProcessId, gm.CurrentGame?.ProcessId);
        Assert.Equal(Cs2.ProcessId, booster.LastExclude);
        Assert.Equal(new[] { 111, 222 }, booster.Suspended);
        Assert.True(engine.IsActive(t.Id));

        await gm.OnGameStoppedAsync(Cs2);
        Assert.Null(gm.CurrentGame);
        Assert.Empty(booster.Suspended);
        Assert.False(engine.IsActive(t.Id));
    }

    [Fact]
    public async Task Profile_never_applies_permanent_tweaks()
    {
        var session = new GateTweak { Hold = false };
        var permanent = new GateTweak { Id = "display.max-refresh", GameSessionOnly = false, Hold = false };
        var engine = new TweakEngine(new ITweak[] { session, permanent }, new InMemorySnapshotStore());
        var settings = new AppSettings
        {
            Games = new[] { new GameProfile { ExecutableName = "cs2.exe", TweakIds = new[] { session.Id, permanent.Id } } },
        }.Normalize();
        var gm = new GameSessionManager(engine, new FakeBooster(), () => settings);
        await gm.OnGameStartedAsync(Cs2);
        Assert.True(engine.IsActive(session.Id));
        Assert.False(engine.IsActive(permanent.Id));
    }

    [Theory]
    [InlineData("cs2.exe")]
    [InlineData("CS2.EXE")]
    [InlineData("cs2")]
    [InlineData(@"C:\Games\cs2.exe")]
    public void Profile_lookup_ignores_case_extension_and_folder(string stored)
    {
        var s = new AppSettings { Games = new[] { new GameProfile { ExecutableName = stored, DisplayName = "x" } } };
        Assert.NotNull(GameSessionManager.FindProfile(s, "cs2.exe"));
    }
}
