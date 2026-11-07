using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PcMax.App.Views;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog() => InitializeComponent();

    public ConfirmDialog(string title, string message, string confirmText, string? cancelText, bool danger) : this()
    {
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        if (danger)
        {
            ConfirmButton.Classes.Remove("primary");
            ConfirmButton.Classes.Add("danger");
        }
        if (cancelText is null) CancelButton.IsVisible = false;
        else CancelButton.Content = cancelText;
    }

    /// <summary>Raised before closing, so a non-modal caller can read the answer.</summary>
    public event Action<bool>? ResultSelected;

    private void OnConfirm(object? sender, RoutedEventArgs e) { ResultSelected?.Invoke(true); Close(true); }
    private void OnCancel(object? sender, RoutedEventArgs e) { ResultSelected?.Invoke(false); Close(false); }
}
