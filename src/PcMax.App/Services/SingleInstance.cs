namespace PcMax.App.Services;

/// <summary>
/// One PC Max per user session: a named mutex in the session-local namespace, named after the user's SID
/// (<c>Local\PcMax-&lt;SID&gt;</c>), so another user or another session cannot pre-create or squat the names to
/// block the app or fake a "show window" signal. A second launch signals the first one (named event) to show its
/// window, then exits.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private static readonly string BaseName = "Local\\PcMax-" + UserKey();
    private static readonly string MutexName = BaseName;
    private static readonly string EventName = BaseName + "-ShowWindow";

    private static string UserKey()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
                if (id.User?.Value is { } sid) return sid;
            }
        }
        catch { /* fall back to the user name */ }
        var name = new string(Environment.UserName.Where(char.IsLetterOrDigit).ToArray());
        return name.Length > 0 ? name : "user";
    }

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
