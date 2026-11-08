using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
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
        Launch(u.AbsoluteUri, "A ligação");
    }

    public void OpenFolder(string path)
    {
        if (Directory.Exists(path)) Launch(Path.GetFullPath(path), "O caminho da pasta");
    }

    /// <summary>
    /// The app runs elevated: on Windows the target is opened by the desktop shell as the normal user, so the
    /// browser or Explorer window never gets administrator rights. If that is not possible, nothing is started
    /// elevated: the text is copied to the clipboard and the user is told.
    /// </summary>
    private void Launch(string target, string what)
    {
        if (OperatingSystem.IsWindows())
        {
            if (ShellDesktopLauncher.TryOpen(target)) return;
            _ = FallbackAsync(target, what);
            return;
        }
        try
        {
            var psi = new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open") { UseShellExecute = false };
            psi.ArgumentList.Add(target);
            Process.Start(psi)?.Dispose();
        }
        catch (Exception ex) { AppLog.Warn($"Não foi possível abrir {target}", ex); }
    }

    private async Task FallbackAsync(string target, string what)
    {
        var copied = false;
        try
        {
            var top = _owner() ?? (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows.FirstOrDefault();
            if (top?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(target);
                copied = true;
            }
        }
        catch (Exception ex) { AppLog.Warn("Não foi possível copiar para a área de transferência", ex); }

        await ShowMessageAsync("Não foi possível abrir",
            copied
                ? $"{what} foi copiado para a área de transferência. Cola-o no navegador ou no Explorador de Ficheiros:\n\n{target}"
                : $"Abre manualmente no navegador ou no Explorador de Ficheiros:\n\n{target}");
    }
}
