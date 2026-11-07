using CommunityToolkit.Mvvm.ComponentModel;

namespace PcMax.App.ViewModels;

public abstract class ViewModelBase : ObservableObject { }

/// <summary>A page shown in the main window's content area.</summary>
public abstract class PageViewModel : ViewModelBase
{
    public abstract string Title { get; }
    public virtual string? Subtitle => null;
    /// <summary>Called each time the user opens the page.</summary>
    public virtual void OnActivated() { }
}

/// <summary>Colour meaning used across the UI. Views map it to brushes.</summary>
public enum Tone { Neutral, Good, Info, Warning, Critical, Accent }
