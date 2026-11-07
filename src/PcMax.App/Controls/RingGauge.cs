using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PcMax.App.Controls;

/// <summary>A circular progress ring (track + value arc, starting at 12 o'clock).</summary>
public class RingGauge : Control
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<RingGauge, double>(nameof(Value));
    public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<RingGauge, double>(nameof(Maximum), 100);
    public static readonly StyledProperty<double> ThicknessProperty = AvaloniaProperty.Register<RingGauge, double>(nameof(Thickness), 10);
    public static readonly StyledProperty<IBrush?> ForegroundProperty = AvaloniaProperty.Register<RingGauge, IBrush?>(nameof(Foreground), Brushes.Cyan);
    public static readonly StyledProperty<IBrush?> TrackBrushProperty = AvaloniaProperty.Register<RingGauge, IBrush?>(nameof(TrackBrush), new SolidColorBrush(Color.Parse("#1E2440")));

    static RingGauge()
    {
        AffectsRender<RingGauge>(ValueProperty, MaximumProperty, ThicknessProperty, ForegroundProperty, TrackBrushProperty);
    }

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Thickness { get => GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public IBrush? TrackBrush { get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= Thickness * 2) return;
        var r = (size - Thickness) / 2;
        var c = new Point(Bounds.Width / 2, Bounds.Height / 2);

        context.DrawEllipse(null, new Pen(TrackBrush, Thickness), c, r, r);

        var frac = Maximum <= 0 ? 0 : Math.Clamp(Value / Maximum, 0, 1);
        if (frac <= 0) return;
        var pen = new Pen(Foreground, Thickness, lineCap: PenLineCap.Round);
        if (frac >= 0.9999)
        {
            context.DrawEllipse(null, pen, c, r, r);
            return;
        }
        var angle = frac * 2 * Math.PI;
        var start = new Point(c.X, c.Y - r);
        var end = new Point(c.X + r * Math.Sin(angle), c.Y - r * Math.Cos(angle));
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(start, false);
            ctx.ArcTo(end, new Size(r, r), 0, angle > Math.PI, SweepDirection.Clockwise);
            ctx.EndFigure(false);
        }
        context.DrawGeometry(null, pen, geo);
    }
}
