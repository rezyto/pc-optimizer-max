namespace PcMax.App.Services;

public interface IDialogService
{
    /// <summary>Shows a yes/no dialog. <paramref name="danger"/> paints the confirm button red.</summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmText = "Continuar", string cancelText = "Cancelar", bool danger = false);
    Task ShowMessageAsync(string title, string message);
    /// <summary>Lets the user pick a game executable. Returns the full path or null.</summary>
    Task<string?> PickExecutableAsync();
    void OpenUrl(string url);
    void OpenFolder(string path);
}
