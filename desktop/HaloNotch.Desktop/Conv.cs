using Avalonia.Data.Converters;

namespace HaloNotch;

/// Small value converters used in XAML.
public static class Conv
{
    public static readonly IValueConverter IsZero = new FuncValueConverter<int, bool>(n => n == 0);
    public static readonly IValueConverter CountOrEmpty = new FuncValueConverter<int, string>(n => n > 0 ? n.ToString() : "");
    /// Pinned marker: check mark right after copying, pin otherwise.
    public static readonly IValueConverter CopiedGlyph = new FuncValueConverter<bool, string>(copied => copied ? "" : "");
    public static readonly IValueConverter CopyGlyph = new FuncValueConverter<bool, string>(copied => copied ? "\uE73E" : "\uE8C8");
    public static readonly IValueConverter Percent = new FuncValueConverter<double, string>(v => $"{Math.Round(v * 100)}");
}
