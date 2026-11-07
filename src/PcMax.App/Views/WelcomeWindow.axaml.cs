using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PcMax.App.Views;

/// <summary>First-run dialog. Returns true when the user wants the first analysis now.</summary>
public partial class WelcomeWindow : Window
{
    public WelcomeWindow() => InitializeComponent();
    private void OnAnalyze(object? sender, RoutedEventArgs e) => Close(true);
    private void OnLater(object? sender, RoutedEventArgs e) => Close(false);
}
