using System.Globalization;

namespace PcMax.App.Services;

/// <summary>pt-PT formatting helpers.</summary>
public static class Format
{
    public static readonly CultureInfo Pt = CreatePt();

    private static CultureInfo CreatePt()
    {
        try { return CultureInfo.GetCultureInfo("pt-PT"); }
        catch (CultureNotFoundException) { return CultureInfo.InvariantCulture; }
    }

    public static string Bytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = Math.Max(0, bytes);
        var u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return u == 0 ? $"{v:0} {units[u]}" : v.ToString(v >= 100 ? "0" : "0.0", Pt) + " " + units[u];
    }

    /// <summary>"1 ponto" / "3 pontos".</summary>
    public static string Count(int n, string singular, string plural) => $"{n} {(n == 1 ? singular : plural)}";

    public static string Ms(double? v) => v is { } x ? x.ToString(x >= 100 ? "0" : "0.0", Pt) + " ms" : "—";
    public static string Percent(double? v) => v is { } x ? x.ToString("0", Pt) + "%" : "—";
    public static string Temp(double? v) => v is { } x ? x.ToString("0", Pt) + " °C" : "—";
}
