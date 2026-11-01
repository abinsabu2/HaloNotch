using System.Globalization;

namespace HaloNotch;

/// Display formatting shared by the views.
public static class Fmt
{
    /// "2h 05m", "12m", "<1m" (same as the Mac app).
    public static string Duration(double seconds)
    {
        int minutes = (int)(seconds / 60);
        if (minutes < 1) return "<1m";
        if (minutes < 60) return $"{minutes}m";
        return $"{minutes / 60}h {minutes % 60:00}m";
    }

    /// 382k, 12.9M
    public static string Tokens(long n) =>
        n >= 1_000_000 ? (n / 1_000_000.0).ToString("0.#", CultureInfo.CurrentCulture) + "M"
        : n >= 1_000 ? (n / 1_000.0).ToString(n >= 100_000 ? "0" : "0.#", CultureInfo.CurrentCulture) + "k"
        : n.ToString(CultureInfo.CurrentCulture);

    public static string Time(DateTime t) => t.ToString("t", CultureInfo.CurrentCulture);
    public static string ShortDate(DateTime t) => t.ToString("ddd d. MMM", CultureInfo.CurrentCulture);
    public static string Degrees(double v) => $"{Math.Round(v):0}°";
}
