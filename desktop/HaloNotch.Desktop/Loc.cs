using System.Globalization;
using System.Text;
using System.Text.Json;

namespace HaloNotch;

/// UI texts. Key = English text, like L() on the Mac. Sources: shared/translations.json (exported from the
/// Mac app) + translations.desktop.json (Windows/Linux-only texts, same format).
public static class Loc
{
    record Data(string[] Columns, Dictionary<string, string[]> Table);

    public static readonly (string Code, string Name)[] Languages =
    [
        ("system", "System"), ("en", "English"), ("fr", "Français"), ("de", "Deutsch"), ("es", "Español"),
        ("pt-BR", "Português (Brasil)"), ("it", "Italiano"), ("ja", "日本語"), ("nl", "Nederlands"),
        ("ko", "한국어"), ("pl", "Polski"), ("zh-Hans", "简体中文"),
    ];

    static readonly Data Shared = Load("translations.json");
    static readonly Data Desktop = Load("translations.desktop.json");

    /// Language used by this run (fixed at launch; changing it needs a relaunch, like on the Mac).
    public static readonly string Current = Resolve(Prefs.Get(Prefs.Language, "system"));
    static readonly int Column = Array.IndexOf(Shared.Columns, Current);

    /// Call once at launch, before any UI: dates and numbers follow the chosen language too (like the Mac app).
    public static void ApplyCulture()
    {
        if (Prefs.Get(Prefs.Language, "system") == "system") return;
        var culture = CultureInfo.GetCultureInfo(Current);
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = culture;
    }

    public static string L(string key) =>
        Column < 0 ? key
        : Shared.Table.TryGetValue(key, out var t) ? t[Column]
        : Desktop.Table.TryGetValue(key, out var d) ? d[Column]
        : key;

    /// Mac-style format strings: %@ / %d → arguments in order, %% → %.
    public static string LF(string key, params object[] args)
    {
        var format = L(key);
        var sb = new StringBuilder();
        int next = 0;
        for (int i = 0; i < format.Length; i++)
        {
            if (format[i] == '%' && i + 1 < format.Length)
            {
                char c = format[i + 1];
                if (c == '%') { sb.Append('%'); i++; continue; }
                if (c is '@' or 'd') { sb.Append(next < args.Length ? Convert.ToString(args[next++], CultureInfo.CurrentCulture) : ""); i++; continue; }
            }
            sb.Append(format[i]);
        }
        return sb.ToString();
    }

    static Data Load(string resource)
    {
        using var s = typeof(Loc).Assembly.GetManifestResourceStream(resource)!;
        return JsonSerializer.Deserialize<Data>(s, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    /// "system" → UI culture; "de-DE" → "de", "pt-PT" → "pt-BR", "zh-CN" → "zh-Hans" (same rules as the Mac app).
    static string Resolve(string choice)
    {
        var culture = choice == "system" ? CultureInfo.CurrentUICulture.Name : choice;
        return culture.StartsWith("pt") ? "pt-BR" : culture.StartsWith("zh") ? "zh-Hans" : culture.Split('-')[0];
    }
}
