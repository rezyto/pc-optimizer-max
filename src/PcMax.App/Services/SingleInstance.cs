namespace PcMax.App.Services;

/// <summary>
/// One PC Max per machine: a named mutex. A second launch signals the first one (named event)
/// to show its window, then exits.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = "Global\\PcMax-SingleInstance";
    private const string EventName = "Global\\PcMax-ShowWindow";

    private readonly Mutex? _mutex;
    private readonly EventWaitHandle? _showEvent;
    private volatile bool _disposed;

    private SingleInstance(Mutex? mutex, bool isFirst, EventWaitHandle? showEvent)
    {
        _mutex = mutex;
        IsFirst = isFirst;
        _showEvent = showEvent;
    }

    public bool IsFirst { get; }

    public static SingleInstance Acquire()
    {
        Mutex? mutex = null;
        bool created;
        try
        {
            mutex = new Mutex(true, MutexName, out created);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException or WaitHandleCannotBeOpenedException)
        {
            AppLog.Warn("Não foi possível verificar se a app já está aberta", ex);
            return new SingleInstance(null, true, null); // never block the user from opening the app
        }
        if (!created)
        {
            mutex.Dispose();
            return new SingleInstance(null, false, null);
        }

        EventWaitHandle? ev = null;
        try { ev = new EventWaitHandle(false, EventResetMode.AutoReset, EventName); }
        catch (Exception ex) when (ex is PlatformNotSupportedException or UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException)
        {
            // Named events are Windows-only; on other systems a second launch just exits.
        }
        return new SingleInstance(mutex, true, ev);
    }

    /// <summary>Asks the running instance to show its window.</summary>
    public static void SignalExisting()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            if (EventWaitHandle.TryOpenExisting(EventName, out var ev))
                using (ev) ev.Set();
        }
        catch (Exception ex) { AppLog.Warn("Não foi possível chamar a janela já aberta", ex); }
    }

    /// <summary>Calls <paramref name="onShow"/> (on a background thread) whenever another launch signals us.</summary>
    public void ListenForShow(Action onShow)
    {
        if (_showEvent is null) return;
        var t = new Thread(() =>
        {
            while (!_disposed)
            {
                try
                {
                    if (!_showEvent.WaitOne(TimeSpan.FromSeconds(1))) continue;
                }
                catch (ObjectDisposedException) { return; }
                if (!_disposed) onShow();
            }
        }) { IsBackground = true, Name = "PcMax-ShowListener" };
        t.Start();
    }

    public void Dispose()
    {
        _disposed = true;
        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
        _showEvent?.Dispose();
    }
}
