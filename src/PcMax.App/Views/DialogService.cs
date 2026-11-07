using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using PcMax.App.Services;

namespace PcMax.App.Views;

public sealed class DialogService : IDialogService
{
    private readonly Func<Window?> _owner;
    private bool _messageOpen;

    public DialogService(Func<Window?> owner) => _owner = owner;

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText = "Continuar", string cancelText = "Cancelar", bool danger = false)
    {
        var dlg = new ConfirmDialog(title, message, confirmText, cancelText, danger);
        return await ShowAsync(dlg);
    }

    public async Task ShowMessageAsync(string title, string message)
    {
        if (_messageOpen) return; // never stack error dialogs
        _messageOpen = true;
        try { await ShowAsync(new ConfirmDialog(title, message, "OK", null, false)); }
        finally { _messageOpen = false; }
    }

    private async Task<bool> ShowAsync(Window dlg)
    {
        var owner = _owner();
        if (owner is not null) return await dlg.ShowDialog<bool?>(owner) == true;
        // No visible main window (e.g. from the tray): show on its own.
        dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dlg.ShowInTaskbar = true;
        dlg.Topmost = true;
        var tcs = new TaskCompletionSource<bool>();
        bool? result = null;
        dlg.Closed += (_, _) => tcs.TrySetResult(result == true);
        if (dlg is ConfirmDialog cd) cd.ResultSelected += r => result = r;
        dlg.Show();
        return await tcs.Task;
    }

    public async Task<string?> PickExecutableAsync()
    {
        var owner = _owner();
        if (owner is null) return null;
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Escolhe o ficheiro do jogo (.exe)",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Jogo (.exe)") { Patterns = new[] { "*.exe" } } },
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    public void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeHttp)) return;
        Launch(u.AbsoluteUri);
    }

    public void OpenFolder(string path)
    {
        if (Directory.Exists(path)) Launch(path);
    }

    private static void Launch(string target)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            else
                Process.Start(OperatingSystem.IsMacOS() ? "open" : "xdg-open", target);
        }
        catch (Exception ex) { AppLog.Warn($"Não foi possível abrir {target}", ex); }
    }
}
