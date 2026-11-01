using System.Globalization;
using System.Text.Json;

namespace HaloNotch;

/// UI texts from shared/translations.json (exported from the Mac app). Key = English text, like L() on the Mac.
static class Loc
{
    record Data(string[] Columns, Dictionary<string, string[]> Table);

    static readonly Data data = Load();
    static readonly int column = Array.IndexOf(data.Columns, Resolve(CultureInfo.CurrentUICulture.Name));

    public static string L(string key) =>
        column >= 0 && data.Table.TryGetValue(key, out var t) ? t[column] : key;

    static Data Load()
    {
        using var s = typeof(Loc).Assembly.GetManifestResourceStream("translations.json")!;
        return JsonSerializer.Deserialize<Data>(s, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    /// "de-DE" → "de", "pt-PT" → "pt-BR", "zh-CN" → "zh-Hans" (same rules as the Mac app). English → -1 → key.
    static string Resolve(string culture) =>
        culture.StartsWith("pt") ? "pt-BR" : culture.StartsWith("zh") ? "zh-Hans" : culture.Split('-')[0];
}
