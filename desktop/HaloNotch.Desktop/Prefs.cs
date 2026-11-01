using System.Text.Json;
using System.Text.Json.Nodes;

namespace HaloNotch;

/// Settings (the Mac app's UserDefaults): %APPDATA%\HaloNotch\settings.json. Keys match the Mac app.
public static class Prefs
{
    public const string VolumeHUD = "hud.volume";
    public const string BrightnessHUD = "hud.brightness";
    public const string IgnorePasswordManagers = "clipboard.ignorePasswordManagers";
    public const string PasteDirectly = "clipboard.pasteDirectly";
    public const string RealEqualizer = "media.realEqualizer";
    public const string DisplayMode = "display.mode";              // all | main
    public const string DashboardHidden = "dashboard.hiddenSections";
    public const string UsageEnabled = "usage.enabled";            // opt-in: read Claude Code / Codex logs
    public const string Language = "app.language";                // system | en | de | …
    public const string LanguagePrompted = "app.languagePrompted";
    public const string CalendarFeeds = "calendar.feeds";          // ICS URLs (Windows: no shared calendar store)

    /// HALONOTCH_DATA overrides the folder (tests / snapshots never touch the real settings).
    public static readonly string Dir = Environment.GetEnvironmentVariable("HALONOTCH_DATA") is { Length: > 0 } custom ? custom
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HaloNotch");
    static readonly string FilePath = Path.Combine(Dir, "settings.json");
    static readonly JsonObject Root = Load();

    public static T Get<T>(string key, T fallback)
    {
        try { return Root[key] is { } node ? node.Deserialize<T>() ?? fallback : fallback; }
        catch (JsonException) { return fallback; }
    }

    public static bool Has(string key) => Root.ContainsKey(key);

    public static void Set<T>(string key, T value)
    {
        Root[key] = JsonSerializer.SerializeToNode(value);
        Save();
    }

    public static void Remove(string key)
    {
        if (Root.Remove(key)) Save();
    }

    static JsonObject Load()
    {
        try { return JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject ?? new JsonObject(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new JsonObject(); }
    }

    static void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            WriteAtomic(FilePath, Root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* next change retries */ }
    }

    /// Write to a temp file, then replace – a crash mid-write never leaves a half file.
    public static void WriteAtomic(string path, string text)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, text);
        File.Move(tmp, path, overwrite: true);
    }
}
