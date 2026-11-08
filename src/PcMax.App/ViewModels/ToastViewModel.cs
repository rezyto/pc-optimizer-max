using PcMax.Core.Alerts;

namespace PcMax.App.ViewModels;

public sealed class ToastViewModel
{
    public ToastViewModel(Alert alert, string? actionText = null, Action? action = null)
    {
        Alert = alert;
        ActionText = action is null ? null : actionText;
        Action = action;
        (Tone, Icon, Label) = alert.Severity switch
        {
            AlertSeverity.Critical => (Tone.Critical, "Icon.Warning", "CRÍTICO"),
            AlertSeverity.Warning => (Tone.Warning, "Icon.Warning", "AVISO"),
            _ => (Tone.Accent, "Icon.Info", "PC MAX"),
        };
    }

    public Alert Alert { get; }
    public string Title => Alert.Title;
    public string Message => Alert.Message;
    public Tone Tone { get; }
    public string Icon { get; }
    public string Label { get; }
    /// <summary>Optional button (e.g. "Instalar agora"). Toasts with a button stay until closed.</summary>
    public string? ActionText { get; }
    public Action? Action { get; }
    public bool HasAction => Action is not null;
    public bool IsSticky => Alert.Severity == AlertSeverity.Critical || HasAction;
    public bool ShowHint => Alert.Severity == AlertSeverity.Critical;
    public string Hint => ShowHint ? "Clica para abrir o PC Max" : "";
}
