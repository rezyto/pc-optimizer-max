using Avalonia;
using Avalonia.Threading;
using PcMax.App.Services;
using PcMax.App.ViewModels;
using PcMax.Core.Alerts;
using PcMax.Core.Platform;

namespace PcMax.App.Views;

/// <summary>
/// Pop-ups in the bottom-right corner of the primary screen: up to 3 stacked, hidden after 6 s
/// (critical ones stay until clicked). Respects "notifications on" and "quiet while gaming".
/// </summary>
public sealed class ToastNotifier : IActionNotifier
{
    private const int MaxVisible = 3;
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(6);
    private const double Gap = 4, EdgeMargin = 12;

    private readonly SettingsService _settings;
    private readonly Func<bool> _gameActive;
    private readonly Action _onActivate;
    private readonly List<ToastWindow> _open = new();

    public ToastNotifier(SettingsService settings, Func<bool> gameActive, Action onActivate)
    {
        _settings = settings;
        _gameActive = gameActive;
        _onActivate = onActivate;
    }

    /// <summary>Thread-safe.</summary>
    public void Show(Alert alert) => Show(alert, null, null);

    /// <summary>Thread-safe. The pop-up gets a button that runs <paramref name="action"/> (UI thread) and closes it.</summary>
    public void Show(Alert alert, string? actionText, Action? action)
    {
        if (!NotificationPolicy.ShouldShow(_settings.Current, _gameActive(), alert.Severity)) return;
        Dispatcher.UIThread.Post(() =>
        {
            try { Add(alert, actionText, action); }
            catch (Exception ex) { AppLog.Warn("Falha ao mostrar notificação", ex); }
        });
    }

    void IActionNotifier.Show(Alert alert, string actionText, Action action) => Show(alert, actionText, action);

    private void Add(Alert alert, string? actionText, Action? action)
    {
        // Same alert already showing: replace it.
        // Windows are taken out of the list here, not only in Closed: the loop must end even if Close() does not
        // raise Closed synchronously (or at all, for a window that failed to open).
        foreach (var w in _open.Where(w => (w.DataContext as ToastViewModel)?.Alert.Key == alert.Key).ToList())
        {
            _open.Remove(w);
            CloseQuietly(w);
        }
        while (_open.Count >= MaxVisible)
        {
            var victim = _open.FirstOrDefault(w => (w.DataContext as ToastViewModel)?.IsSticky != true) ?? _open[0];
            _open.Remove(victim);
            CloseQuietly(victim);
        }

        var vm = new ToastViewModel(alert, actionText, action);
        var win = new ToastWindow { DataContext = vm };
        win.Activated2 += (_, _) => _onActivate();
        win.Closed += (_, _) => { _open.Remove(win); Layout(); };
        _open.Add(win);
        win.Opened += (_, _) => Layout();
        win.SizeChanged += (_, _) => Layout();
        win.Show();
        Layout();

        if (!vm.IsSticky)
            DispatcherTimer.RunOnce(() => { if (_open.Contains(win)) win.Close(); }, Lifetime);
    }

    private static void CloseQuietly(ToastWindow w)
    {
        try { w.Close(); }
        catch (Exception ex) { AppLog.Warn("Falha ao fechar notificação", ex); }
    }

    /// <summary>Newest at the bottom, older ones pushed up.</summary>
    private void Layout()
    {
        if (_open.Count == 0) return;
        var screen = _open[0].Screens.Primary ?? _open[0].Screens.All.FirstOrDefault();
        if (screen is null) return;
        var area = screen.WorkingArea;
        var scale = screen.Scaling;
        var bottom = area.Bottom - EdgeMargin * scale;
        for (var i = _open.Count - 1; i >= 0; i--)
        {
            var w = _open[i];
            var width = (w.Bounds.Width > 0 ? w.Bounds.Width : w.Width) * scale;
            var height = (w.Bounds.Height > 0 ? w.Bounds.Height : 120) * scale;
            var top = bottom - height;
            w.Position = new PixelPoint((int)(area.Right - EdgeMargin * scale - width), (int)top);
            bottom = top - Gap * scale;
        }
    }
}
