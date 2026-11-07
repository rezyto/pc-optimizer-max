using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace PcMax.App.Controls;

/// <summary>Small line graph of recent values: a Polyline (and a soft filled area) inside a Canvas.</summary>
public class Sparkline : UserControl
{
    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty =
        AvaloniaProperty.Register<Sparkline, IReadOnlyList<double>?>(nameof(Values));
    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<Sparkline, double>(nameof(Maximum), double.NaN);
    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<Sparkline, double>(nameof(Minimum), 0);
    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<Sparkline, IBrush?>(nameof(Stroke), Brushes.Cyan);
    public static readonly StyledProperty<int> CapacityProperty =
        AvaloniaProperty.Register<Sparkline, int>(nameof(Capacity), 60);

    private readonly Canvas _canvas = new() { ClipToBounds = true };
    private readonly Polyline _line = new() { StrokeThickness = 2, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round };
    private readonly Polygon _area = new() { Opacity = 0.16 };

    public Sparkline()
    {
        _canvas.Children.Add(_area);
        _canvas.Children.Add(_line);
        Content = _canvas;
        _line.Bind(Shape.StrokeProperty, this.GetObservable(StrokeProperty));
        _area.Bind(Shape.FillProperty, this.GetObservable(StrokeProperty));
    }

    public IReadOnlyList<double>? Values { get => GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public IBrush? Stroke { get => GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public int Capacity { get => GetValue(CapacityProperty); set => SetValue(CapacityProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValuesProperty || change.Property == MaximumProperty || change.Property == MinimumProperty ||
            change.Property == CapacityProperty || change.Property == BoundsProperty)
            Redraw();
    }

    private void Redraw()
    {
        var v = Values;
        double w = Bounds.Width, h = Bounds.Height;
        if (v is null || v.Count < 2 || w <= 0 || h <= 0)
        {
            _line.Points = new AvaloniaList<Point>();
            _area.Points = new AvaloniaList<Point>();
            return;
        }
        var max = double.IsNaN(Maximum) ? Math.Max(v.Max() * 1.15, Minimum + 1) : Maximum;
        var min = Minimum;
        var cap = Math.Max(Capacity, v.Count);
        var step = w / (cap - 1);
        var x0 = w - (v.Count - 1) * step; // newest value at the right edge
        var pts = new AvaloniaList<Point>();
        for (var i = 0; i < v.Count; i++)
        {
            var y = h - 2 - (Math.Clamp(v[i], min, max) - min) / (max - min) * (h - 4);
            pts.Add(new Point(x0 + i * step, y));
        }
        _line.Points = pts;
        var area = new AvaloniaList<Point>(pts) { new Point(pts[^1].X, h), new Point(pts[0].X, h) };
        _area.Points = area;
    }
}
