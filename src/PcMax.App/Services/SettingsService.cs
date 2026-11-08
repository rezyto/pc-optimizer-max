using PcMax.Core.Settings;

namespace PcMax.App.Services;

public sealed record SettingsChange(AppSettings Old, AppSettings New);

/// <summary>
/// Holds the current settings in memory. Every change is applied at once and saved to disk
/// shortly after (debounced), so dragging a value does not write the file 20 times.
/// </summary>
public sealed class SettingsService : IDisposable
{
    private readonly SettingsStore _store;
    private readonly object _gate = new();
    // Serializes writes: two flushes (timer + exit) must never run at once, nor an older copy land after a newer one.
    private readonly object _saveGate = new();
    private bool _disposed;
    private readonly Timer _saveTimer;
    private AppSettings _current;
    private bool _dirty;

    public SettingsService(SettingsStore store, AppSettings? initial = null)
    {
        _store = store;
        _current = (initial ?? store.Load()).Normalize();
        _saveTimer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public TimeSpan SaveDelay { get; init; } = TimeSpan.FromMilliseconds(600);

    public AppSettings Current { get { lock (_gate) return _current; } }

    /// <summary>Raised on the thread that made the change.</summary>
    public event EventHandler<SettingsChange>? Changed;

    public void Update(Func<AppSettings, AppSettings> change)
    {
        AppSettings old, next;
        lock (_gate)
        {
            old = _current;
            next = change(old).Normalize();
            if (next.SameAs(old)) return;
            _current = next;
            _dirty = true;
            if (!_disposed) _saveTimer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
        }
        Changed?.Invoke(this, new SettingsChange(old, next));
    }

    public void Flush()
    {
        lock (_saveGate)
        {
            AppSettings toSave;
            lock (_gate)
            {
                if (!_dirty) return;
                _dirty = false;
                toSave = _current;
            }
            try { _store.Save(toSave); }
            catch (Exception ex)
            {
                lock (_gate) _dirty = true; // try again on the next flush
                AppLog.Error("Falha ao guardar definições", ex);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate) _disposed = true;
        _saveTimer.Dispose();
        Flush();
    }
}
