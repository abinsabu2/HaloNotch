using System.Text.RegularExpressions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using static HaloNotch.Loc;

namespace HaloNotch;

/// Column in the clipboard view.
public enum ClipKind { Text, Code, Link, File, Media, Color }

/// One clipboard entry: text, image or files. Port of the Mac app's ClipItem (same classification rules).
public sealed class ClipItem : Observable
{
    public Guid Id { get; } = Guid.NewGuid();
    public string? Text { get; }
    public string[]? Files { get; }
    public byte[]? Dib { get; }                  // original image data, written back on copy
    public Bitmap? Image { get; }                // preview (images and image files)
    public DateTime Date { get; } = DateTime.Now;
    public ClipKind Kind { get; }
    /// Syntax-coloured preview for code items (made once at capture).
    public IReadOnlyList<(string Text, Color Color)>? Highlighted { get; }
    public Color? Swatch { get; }

    bool pinned;
    public bool IsPinned { get => pinned; set { if (Set(ref pinned, value)) Changed(nameof(PinLabel)); } }
    string? shortcut;
    /// "1"–"9", "0" for the ten newest items (⌃⌥V keyboard mode).
    public string? Shortcut { get => shortcut; set => Set(ref shortcut, value); }
    bool copied;
    public bool IsCopied { get => copied; set => Set(ref copied, value); }

    public ClipItem(string? text = null, string[]? files = null, byte[]? dib = null, Bitmap? image = null, bool isPinned = false)
    {
        Text = text; Files = files; Dib = dib; Image = image; pinned = isPinned;
        Kind = Classify();
        if (Kind == ClipKind.Code && text != null) Highlighted = Highlight(Trimmed(text, 400));
        if (Kind == ClipKind.Color && text != null) Swatch = ParseColor(text.Trim().ToLowerInvariant());
    }

    public bool IsImage => Image != null;
    public bool IsText => Text != null;
    public bool IsFiles => Files != null;
    // Which row layout the views show.
    public bool ShowText => Text != null && Kind is ClipKind.Text or ClipKind.Link;
    public bool ShowCode => Kind == ClipKind.Code;
    public bool ShowColor => Kind == ClipKind.Color;
    public bool ShowImage => Image != null;
    public bool ShowFiles => Files != null && Image == null;
    public IBrush SwatchBrush => new SolidColorBrush(Swatch ?? Colors.Gray);
    public string PinLabel => IsPinned ? L("Unpin") : L("Pin");

    public string SearchText =>
        Text ?? (Files != null ? string.Join(" ", Files.Select(Path.GetFileName)) : L("Image"));

    /// One-line-ish preview for rows.
    public string Preview =>
        Text != null ? Trimmed(Text, 300)
        : Files != null ? (Files.Length == 1 ? Path.GetFileName(Files[0]) : LF("%d files", Files.Length))
        : L("Image");

    public string? Detail =>
        Files is { Length: > 0 } f ? Path.GetDirectoryName(f[0]) : Text != null && Kind == ClipKind.Link ? Host(Text) : null;

    static string Trimmed(string s, int max) { s = s.Trim(); return s.Length > max ? s[..max] + "…" : s; }
    static string? Host(string url) => Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u) ? u.Host : null;

    // MARK: Classification

    static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".heic", ".tif", ".tiff", ".svg", ".ico",
        ".mp4", ".mov", ".m4v", ".avi", ".mkv", ".webm", ".mp3", ".wav", ".m4a", ".aac", ".flac", ".ogg",
    };

    public static bool IsMediaFile(string path) => MediaExtensions.Contains(Path.GetExtension(path));

    ClipKind Classify()
    {
        if (Image != null && Files == null) return ClipKind.Media;
        if (Files != null) return Files.All(IsMediaFile) ? ClipKind.Media : ClipKind.File;
        var t = (Text ?? "").Trim();
        if (IsLink(t)) return ClipKind.Link;
        if (IsColor(t)) return ClipKind.Color;
        if (IsCode(t)) return ClipKind.Code;
        return ClipKind.Text;
    }

    public static bool IsLink(string t) =>
        (t.StartsWith("http://") || t.StartsWith("https://")) && !t.Any(char.IsWhiteSpace);

    static readonly Regex ColorPattern = new(
        @"^(#([0-9a-f]{3,4}|[0-9a-f]{6}|[0-9a-f]{8})|(rgba?|hsla?|hwb|lab|lch|oklab|oklch|color)\([^()]{1,80}\))$",
        RegexOptions.IgnoreCase);

    public static bool IsColor(string t) => t.Length <= 90 && ColorPattern.IsMatch(t);

    static readonly string[] CodeHints = ["func ", "function ", "=>", "->", "return ", "const ", "let ", "var ", "import ",
        "def ", "class ", "struct ", "#include", "<?php", "</", "/>", "SELECT ", "if (", "for (", "){", ") {", "};", "::", "!==", "==="];

    /// `.env` / shell assignments: `KEY=value`, `export KEY=value`.
    static readonly Regex EnvLine = new(@"^(export\s+)?[A-Za-z_][A-Za-z0-9_.]*=");
    /// YAML mapping line: `key:`, `key: value`, `- key: value`, `"quoted key": value`.
    static readonly Regex YamlKey = new(@"^(-\s+)?(""[^""]+""|'[^']+'|[A-Za-z0-9_][\w.\-/]*)\s*:(\s|$)");

    /// Heuristic: CSS declarations, braces/semicolons, common keywords, .env or YAML.
    public static bool IsCode(string t)
    {
        var lines = t.Split('\n', '\r').Where(l => l.Length > 0).ToArray();
        var content = lines.Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToArray();
        if (content.Length > 0 && content.All(l => EnvLine.IsMatch(l))) return true;
        if (content.Length >= 2)
        {
            bool IsKey(string l) => YamlKey.IsMatch(l);
            bool IsOther(string l) => l.StartsWith("- ") || l is "-" or "---" or "...";
            if (content.Count(IsKey) >= 2 && content.All(l => IsKey(l) || IsOther(l))) return true;
        }
        int hits = CodeHints.Count(t.Contains);
        int structural = lines.Select(l => l.Trim()).Count(l => l.EndsWith(';') || l.EndsWith('{') || l == "}" || l.EndsWith("};"));
        if (lines.Length > 1) return hits >= 2 || structural >= 2 || (hits >= 1 && structural >= 1);
        return (t.EndsWith(';') && (t.Contains(':') || t.Contains('('))) || hits >= 2;
    }

    // MARK: Syntax colouring

    static readonly Color BaseText = Color.FromArgb(217, 255, 255, 255);

    /// Later rules win: keys/calls < keywords < numbers < strings < comments.
    static readonly (Regex Rule, Color Color)[] SyntaxRules =
    [
        (new(@"(?m)^\s*(?:export\s+|-\s+)?[A-Za-z_][\w.-]*(?=\s*[=:])"), Rgb(0.55, 0.85, 0.95)),
        (new(@"\b[A-Za-z_]\w*(?=\()"), Rgb(0.45, 0.72, 1.0)),
        (new(@"\b(func|function|return|const|let|var|import|export|from|def|class|struct|enum|interface|type|if|else|for|while|switch|case|in|of|new|await|async|public|private|static|true|false|null|nil|undefined|self|this)\b"),
            Rgb(0.98, 0.47, 0.70)),
        (new(@"(?<![\w#])-?\d+(\.\d+)?(px|rem|em|vh|vw|ms|s|%|deg)?\b"), Rgb(1.0, 0.72, 0.35)),
        (new(@"""[^""\n]*""|'[^'\n]*'|`[^`]*`"), Rgb(0.56, 0.88, 0.55)),
        (new(@"(?m)((?<!:)//.*$|^\s*#.*$|/\*[\s\S]*?\*/)"), Color.FromArgb(97, 255, 255, 255)),
    ];

    static Color Rgb(double r, double g, double b) => Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));

    static List<(string, Color)> Highlight(string code)
    {
        var colors = Enumerable.Repeat(BaseText, code.Length).ToArray();
        foreach (var (rule, color) in SyntaxRules)
            foreach (Match m in rule.Matches(code))
                for (int i = m.Index; i < m.Index + m.Length; i++) colors[i] = color;
        var runs = new List<(string, Color)>();
        int start = 0;
        for (int i = 1; i <= code.Length; i++)
        {
            if (i < code.Length && colors[i] == colors[start]) continue;
            runs.Add((code[start..i], colors[start]));
            start = i;
        }
        return runs;
    }

    // MARK: Colour parsing (CSS Color 4)

    public static Color? ParseColor(string t)
    {
        if (t.StartsWith('#'))
        {
            var hex = t[1..];
            if (hex.Length <= 4) hex = string.Concat(hex.Select(c => $"{c}{c}"));
            if (!ulong.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var v)) return null;
            bool alpha = hex.Length == 8;
            return Color.FromArgb(alpha ? (byte)(v & 0xff) : (byte)255,
                (byte)(alpha ? v >> 24 : v >> 16), (byte)(alpha ? v >> 16 : v >> 8), (byte)(alpha ? v >> 8 : v));
        }
        int open = t.IndexOf('(');
        if (open < 0 || !t.EndsWith(')')) return null;
        var fn = t[..open];
        var parts = t[(open + 1)..^1].Split([',', ' ', '/'], StringSplitOptions.RemoveEmptyEntries).ToList();
        var space = fn == "color" && parts.Count > 0 ? parts[0] : "";
        if (fn == "color" && parts.Count > 0) parts.RemoveAt(0);
        var nums = parts.Select(Num).ToList();
        if (nums.Count < 3 || nums[0] is not { } x || nums[1] is not { } y || nums[2] is not { } z) return null;
        double alphaV = nums.Count > 3 ? nums[3]?.V ?? 1 : 1;
        double a = x.V, b = y.V, c = z.V;
        switch (fn)
        {
            case "rgb" or "rgba":
                double S((double V, bool Pct) n) => n.Pct ? n.V : n.V / 255;
                return FromUnit(S(x), S(y), S(z), alphaV);
            case "hsl" or "hsla":
            {
                double l = c, s = b, v = l + s * Math.Min(l, 1 - l), sv = v == 0 ? 0 : 2 * (1 - l / v);
                return WithAlpha(new HsvColor(1, Hue(a), sv, v).ToRgb(), alphaV);
            }
            case "hwb":
            {
                var (w, k) = b + c >= 1 ? (b / (b + c), c / (b + c)) : (b, c);
                var pure = new HsvColor(1, Hue(a), 1, 1).ToRgb();
                double Mix(byte ch) => ch / 255.0 * (1 - w - k) + w;
                return FromUnit(Mix(pure.R), Mix(pure.G), Mix(pure.B), alphaV);
            }
            case "oklab" or "oklch":
            {
                var (oa, ob) = fn == "oklab" ? (y.Pct ? b * 0.4 : b, z.Pct ? c * 0.4 : c) : Polar(y.Pct ? b * 0.4 : b, c);
                return Srgb(OklabToLinear(a, oa, ob), alphaV);
            }
            case "lab" or "lch":
            {
                double L0 = x.Pct ? a * 100 : a;
                var (la, lb) = fn == "lab" ? (y.Pct ? b * 125 : b, z.Pct ? c * 125 : c) : Polar(y.Pct ? b * 150 : b, c);
                return Srgb(LabToLinear(L0, la, lb), alphaV);
            }
            case "color" when space is "srgb" or "display-p3":     // ponytail: display-p3 shown as sRGB (slightly duller)
                return FromUnit(a, b, c, alphaV);
            default: return null;
        }
    }

    static (double V, bool Pct)? Num(string p)
    {
        static double? D(string s) => double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
        if (p == "none") return (0, false);
        if (p.EndsWith('%')) return D(p[..^1]) is { } v ? (v / 100, true) : null;
        if (p.EndsWith("turn")) return D(p[..^4]) is { } t ? (t * 360, false) : null;
        if (p.EndsWith("rad")) return D(p[..^3]) is { } r ? (r * 180 / Math.PI, false) : null;
        if (p.EndsWith("deg")) return D(p[..^3]) is { } g ? (g, false) : null;
        return D(p) is { } n ? (n, false) : null;
    }

    static double Hue(double degrees) { var h = degrees % 360; return h < 0 ? h + 360 : h; }
    static (double, double) Polar(double chroma, double hue) { var r = hue * Math.PI / 180; return (chroma * Math.Cos(r), chroma * Math.Sin(r)); }
    static Color FromUnit(double r, double g, double b, double a) =>
        Color.FromArgb(Byte(a), Byte(r), Byte(g), Byte(b));
    static Color WithAlpha(Color c, double a) => Color.FromArgb(Byte(a), c.R, c.G, c.B);
    static byte Byte(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);

    static (double, double, double) OklabToLinear(double L, double a, double b)
    {
        double l = Math.Pow(L + 0.3963377774 * a + 0.2158037573 * b, 3);
        double m = Math.Pow(L - 0.1055613458 * a - 0.0638541728 * b, 3);
        double s = Math.Pow(L - 0.0894841775 * a - 1.2914855480 * b, 3);
        return (4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
               -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
               -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s);
    }

    static (double, double, double) LabToLinear(double L, double a, double b)
    {
        double fy = (L + 16) / 116, fx = fy + a / 500, fz = fy - b / 200;
        static double Inv(double v) => v > 6.0 / 29 ? v * v * v : 3 * Math.Pow(6.0 / 29, 2) * (v - 4.0 / 29);
        double X = 0.96422 * Inv(fx), Y = Inv(fy), Z = 0.82521 * Inv(fz);
        return (3.1338561 * X - 1.6168667 * Y - 0.4906146 * Z,
               -0.9787684 * X + 1.9161415 * Y + 0.0334540 * Z,
                0.0719453 * X - 0.2289914 * Y + 1.4052427 * Z);
    }

    static Color Srgb((double R, double G, double B) c, double alpha)
    {
        static double Encode(double v) { v = Math.Clamp(v, 0, 1); return v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055; }
        return FromUnit(Encode(c.R), Encode(c.G), Encode(c.B), alpha);
    }

#if DEBUG
    /// Debug self-check for the classification and colour rules (runs once at startup in debug builds).
    public static void SelfCheck()
    {
        System.Diagnostics.Debug.Assert(new ClipItem("https://example.com/a").Kind == ClipKind.Link);
        System.Diagnostics.Debug.Assert(new ClipItem("#ff8800").Kind == ClipKind.Color);
        System.Diagnostics.Debug.Assert(new ClipItem("API_KEY=abc\nexport X=1").Kind == ClipKind.Code);
        System.Diagnostics.Debug.Assert(new ClipItem("Note: buy milk").Kind == ClipKind.Text);
        System.Diagnostics.Debug.Assert(ParseColor("#ff8800") == Color.FromRgb(255, 136, 0));
        System.Diagnostics.Debug.Assert(ParseColor("rgb(255 0 0)") == Color.FromRgb(255, 0, 0));
    }
#endif
}
