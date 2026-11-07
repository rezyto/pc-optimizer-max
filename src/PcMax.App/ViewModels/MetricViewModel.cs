using CommunityToolkit.Mvvm.ComponentModel;

namespace PcMax.App.ViewModels;

/// <summary>One live reading with its last 60 values (one per second) for the sparkline.</summary>
public partial class MetricViewModel : ViewModelBase
{
    public const int HistoryLength = 60;
    private readonly Queue<double> _buffer = new();
    private readonly Func<double, string> _format;

    public MetricViewModel(string title, string icon, double maximum, Func<double, Tone> classify, Func<double, string> format)
    {
        Title = title;
        Icon = icon;
        Maximum = maximum;
        Classify = classify;
        _format = format;
    }

    public string Title { get; }
    public string Icon { get; }
    public double Maximum { get; }
    /// <summary>Colour thresholds; replaceable when the user changes them in settings.</summary>
    public Func<double, Tone> Classify { get; set; }

    [ObservableProperty] private double? _value;
    [ObservableProperty] private string _display = "—";
    [ObservableProperty] private string? _detail;
    [ObservableProperty] private Tone _tone = Tone.Neutral;
    [ObservableProperty] private IReadOnlyList<double> _history = Array.Empty<double>();

    public double GaugeValue => Value ?? 0;

    public void Push(double? value, string? detail = null)
    {
        Value = value;
        Detail = detail;
        if (value is { } v)
        {
            Display = _format(v);
            Tone = Classify(v);
            _buffer.Enqueue(v);
            while (_buffer.Count > HistoryLength) _buffer.Dequeue();
            History = _buffer.ToArray();
        }
        else
        {
            Display = "—";
            Tone = Tone.Neutral;
        }
        OnPropertyChanged(nameof(GaugeValue));
    }

    public static Func<double, Tone> Thresholds(double warn, double crit) =>
        v => v >= crit ? Tone.Critical : v >= warn ? Tone.Warning : Tone.Good;
}
