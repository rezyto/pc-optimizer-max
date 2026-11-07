using PcMax.Core.Alerts;

namespace PcMax.App.ViewModels;

public sealed class ToastViewModel
{
    public ToastViewModel(Alert alert)
    {
        Alert = alert;
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
    public bool IsSticky => Alert.Severity == AlertSeverity.Critical;
    public string Hint => IsSticky ? "Clica para abrir o PC Max" : "";
}
