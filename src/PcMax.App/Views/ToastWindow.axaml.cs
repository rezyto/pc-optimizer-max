using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace PcMax.App.Views;

public partial class ToastWindow : Window
{
    public ToastWindow() => InitializeComponent();

    /// <summary>The user clicked the body (not the close button).</summary>
    public event EventHandler? Activated2;

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.Handled) return;
        Activated2?.Invoke(this, EventArgs.Empty);
        Close();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        Close();
    }
}
