namespace PcMax.App.Services;

/// <summary>Marshals work to the UI thread. Abstracted so ViewModels and services stay testable.</summary>
public interface IUiDispatcher
{
    void Post(Action action);
}

public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => Avalonia.Threading.Dispatcher.UIThread.Post(action);
}

/// <summary>Runs inline (unit tests).</summary>
public sealed class ImmediateDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}
