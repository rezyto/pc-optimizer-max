using PcMax.Core.Tweaks;

namespace PcMax.App.Services.Fakes;

public sealed class FakeTweak : ITweak
{
    private readonly FakeState _state;

    public FakeTweak(FakeState state, string id, string name, string description, RiskLevel risk, TweakCategory category, bool gameSessionOnly)
    {
        _state = state;
        Id = id; Name = name; Description = description; Risk = risk; Category = category; GameSessionOnly = gameSessionOnly;
    }

    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public RiskLevel Risk { get; }
    public TweakCategory Category { get; }
    public bool GameSessionOnly { get; }

    public Task<bool> IsSupportedAsync(CancellationToken ct = default) => Task.FromResult(true);
    public Task<bool> IsAppliedAsync(CancellationToken ct = default) => Task.FromResult(_state.IsApplied(Id));

    public async Task<TweakSnapshot> ApplyAsync(CancellationToken ct = default)
    {
        await Task.Delay(350, ct);
        var prev = new Dictionary<string, string?> { ["applied"] = _state.IsApplied(Id) ? "1" : "0" };
        _state.Set(Id, true);
        return TweakSnapshot.Create(Id, prev);
    }

    public async Task RevertAsync(TweakSnapshot snapshot, CancellationToken ct = default)
    {
        await Task.Delay(200, ct);
        _state.Set(Id, snapshot.PreviousValues.GetValueOrDefault("applied") == "1");
    }
}
