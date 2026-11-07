using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using PcMax.App.ViewModels;

namespace PcMax.App.Views;

/// <summary>Maps a <see cref="Tone"/> to a colour. Parameter "soft" gives a translucent background.</summary>
public sealed class ToneToBrushConverter : IValueConverter
{
    public static readonly ToneToBrushConverter Instance = new();

    public static Color ColorOf(Tone t) => t switch
    {
        Tone.Good => Color.Parse("#22D39A"),
        Tone.Info => Color.Parse("#4DA3FF"),
        Tone.Warning => Color.Parse("#FFB020"),
        Tone.Critical => Color.Parse("#FF4D6A"),
        Tone.Accent => Color.Parse("#00E5FF"),
        _ => Color.Parse("#8A90B2"),
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var c = ColorOf(value is Tone t ? t : Tone.Neutral);
        if (parameter is "soft") c = Color.FromArgb(0x26, c.R, c.G, c.B);
        if (parameter is "border") c = Color.FromArgb(0x55, c.R, c.G, c.B);
        return new SolidColorBrush(c);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Looks up an icon geometry by resource key (e.g. "Icon.Doctor").</summary>
public sealed class IconKeyConverter : IValueConverter
{
    public static readonly IconKeyConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string key && Application.Current?.TryFindResource(key, out var res) == true && res is Geometry g)
            return g;
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
