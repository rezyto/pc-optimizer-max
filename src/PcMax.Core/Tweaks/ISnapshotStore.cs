namespace PcMax.Core.Tweaks;

/// <summary>Durable storage for the snapshots of tweaks that are currently applied.</summary>
public interface ISnapshotStore
{
    IReadOnlyList<TweakSnapshot> LoadActive();
    void SaveActive(IReadOnlyList<TweakSnapshot> snapshots);
}

public sealed class InMemorySnapshotStore : ISnapshotStore
{
    private List<TweakSnapshot> _items = new();
    public IReadOnlyList<TweakSnapshot> LoadActive() => _items.ToList();
    public void SaveActive(IReadOnlyList<TweakSnapshot> snapshots) => _items = snapshots.ToList();
}
