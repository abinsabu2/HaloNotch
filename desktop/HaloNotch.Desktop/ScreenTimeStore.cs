#pragma warning disable CS0649, CS0067 // used by the Windows build only
using System.Diagnostics;
using System.Text.Json;
using Avalonia.Media;
using Avalonia.Threading;
using static HaloNotch.Loc;
#if WINDOWS
using HaloNotch.Win;
#endif

namespace HaloNotch;

public enum AppCategory { Productivity, Development, Communication, Browsing, Entertainment, Creativity, Utilities, Other }

public sealed record AppUsage(string Id, string Name, double Seconds, AppCategory Category, IBrush Color, double Fraction, int Rank)
{
    public string Time => Fmt.Duration(Seconds);
    public string Initial => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";
}

public sealed record CategoryUsage(AppCategory Category, double Seconds, IBrush Color)
{
    public string Title => ScreenTimeStore.Title(Category);
    public string Time => Fmt.Duration(Seconds);
}

/// Time per foreground app. Stored only in %APPDATA%\HaloNotch\screentime.json – nothing leaves the PC.
/// Port of the Mac app's ScreenTimeStore (same file format, idle limit, reset hour, categories).
public sealed class ScreenTimeStore : Observable
{
    const double IdleLimit = 5 * 60, TickSeconds = 5;
    const int KeepDays = 35;

    public sealed class DayUsage
    {
        public Dictionary<string, double> Apps { get; set; } = [];
        public int Switches { get; set; }
        public double[] Hourly { get; set; } = new double[24];
    }

    sealed class FileData
    {
        public Dictionary<string, DayUsage> Days { get; set; } = [];
        public Dictionary<string, string> Names { get; set; } = [];
    }

    static string FilePath => Path.Combine(Prefs.Dir, "screentime.json");
    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    FileData file = new();
    string? currentApp;
    DateTime lastTick = DateTime.Now;
    int saveCounter;
    DispatcherTimer? timer;
    readonly Dictionary<string, AppCategory> detected = [];

    public int ResetHour { get => Prefs.Get("screentime.resetHour", 4); set { Prefs.Set("screentime.resetHour", value); ChangedAll(); } }
    public bool ShowInsights { get => Prefs.Get("screentime.insights", true); set { Prefs.Set("screentime.insights", value); ChangedAll(); } }
    Dictionary<string, string> Overrides => Prefs.Get("screentime.categoryOverrides", new Dictionary<string, string>());
    Dictionary<string, string> Colors => Prefs.Get("screentime.colors", new Dictionary<string, string>());
    HashSet<string> Excluded => Prefs.Get("screentime.excluded", new HashSet<string>());

    public void Start()
    {
        Load();
#if WINDOWS
        Activated(Native.GetForegroundWindow(), countSwitch: false);
        hook = Native.SetWinEventHook(3, 3, IntPtr.Zero, hookProc = (_, _, hwnd, _, _, _, _) => Activated(hwnd, true), 0, 0, 0);  // EVENT_SYSTEM_FOREGROUND, out of context
#endif
        lastTick = DateTime.Now;
        timer = new DispatcherTimer(TimeSpan.FromSeconds(TickSeconds), DispatcherPriority.Background, (_, _) => Tick());
        timer.Start();
        ChangedAll();
    }

#if WINDOWS
    IntPtr hook;
    Native.WinEventProc? hookProc;     // kept alive: called from native code

    void Activated(IntPtr hwnd, bool countSwitch)
    {
        Tick();                                          // book time for the previous app first
        var path = Native.ProcessPath(hwnd);
        if (path == null) return;
        var id = Path.GetFileName(path).ToLowerInvariant();
        if (id == "halonotch.exe" || id == currentApp) return;
        if (!file.Names.ContainsKey(id)) file.Names[id] = Native.AppName(path);
        currentApp = id;
        if (countSwitch && !Excluded.Contains(id)) { Today.Switches++; ChangedAll(); }
    }

    static double IdleSeconds() => Native.IdleSeconds();
#else
    static double IdleSeconds() => 0;
#endif

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Prefs.Dir);
            Prefs.WriteAtomic(FilePath, JsonSerializer.Serialize(file, Json));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Debug.WriteLine($"HaloNotch: could not save screen time – {e.Message}"); }
    }

    void Load()
    {
        try { file = JsonSerializer.Deserialize<FileData>(File.ReadAllText(FilePath), Json) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { file = new(); }
        var cutoff = DayKey(DateTime.Now.AddDays(-KeepDays));
        foreach (var key in file.Days.Keys.Where(k => string.CompareOrdinal(k, cutoff) < 0).ToList()) file.Days.Remove(key);
    }

    void Tick()
    {
        var now = DateTime.Now;
        // Capped: after sleep the gap is not counted.
        var elapsed = Math.Clamp((now - lastTick).TotalSeconds, 0, TickSeconds * 2);
        lastTick = now;
        if (IdleSeconds() >= IdleLimit || currentApp is not { } id || Excluded.Contains(id)) return;
        var day = Today;
        day.Apps[id] = day.Apps.GetValueOrDefault(id) + elapsed;
        day.Hourly[now.Hour] += elapsed;
        ChangedAll();
        if (++saveCounter >= 12) { saveCounter = 0; Save(); }    // ~ once a minute
    }

    /// Day key with configurable reset hour (4 → usage until 03:59 counts for the previous day).
    string DayKey(DateTime d) => d.AddHours(-ResetHour).ToString("yyyy-MM-dd");

    DayUsage Today
    {
        get
        {
            var key = DayKey(DateTime.Now);
            if (!file.Days.TryGetValue(key, out var day)) file.Days[key] = day = new DayUsage();
            return day;
        }
    }

    DayUsage Yesterday => file.Days.GetValueOrDefault(DayKey(DateTime.Now.AddDays(-1))) ?? new DayUsage();

    // MARK: Queries for the views

    public List<AppUsage> TodayApps
    {
        get
        {
            var excluded = Excluded;
            var apps = Today.Apps.Where(a => !excluded.Contains(a.Key)).OrderByDescending(a => a.Value).ToList();
            double max = apps.Count > 0 ? apps[0].Value : 1;
            return apps.Select((a, i) => new AppUsage(a.Key, Name(a.Key), a.Value, Category(a.Key), Brush(Category(a.Key)), a.Value / max, i + 1)).ToList();
        }
    }

    public List<AppUsage> TopApps => TodayApps.Take(7).ToList();
    public List<CategoryUsage> TodayCategories =>
        TodayApps.GroupBy(a => a.Category).Select(g => new CategoryUsage(g.Key, g.Sum(a => a.Seconds), Brush(g.Key)))
                 .Where(c => c.Seconds > 0).OrderByDescending(c => c.Seconds).ToList();
    public double TodayTotal => TodayApps.Sum(a => a.Seconds);
    public string TotalText => Fmt.Duration(TodayTotal);
    public bool NoData => TodayTotal < 60;
    public string AppCount => LF("%d apps", TodayApps.Count);
    public string Switches => Today.Switches.ToString();
    public double[] Hourly => Today.Hourly;
    public AppUsage? TopApp => TodayApps.FirstOrDefault();
    public string TopAppText => TopApp is { } a ? $"{a.Name} · {a.Time}" : "";

    public string VsYesterday
    {
        get
        {
            var y = Yesterday.Apps.Where(a => !Excluded.Contains(a.Key)).Sum(a => a.Value);
            if (y < 60) return "—";
            var diff = TodayTotal - y;
            return (diff >= 0 ? "+" : "−") + Fmt.Duration(Math.Abs(diff));
        }
    }

    public string Insight => TodayCategories.FirstOrDefault() is { } top && TodayTotal > 0
        ? LF("Mostly %@ today (%d %%)", Title(top.Category), (int)Math.Round(top.Seconds / TodayTotal * 100))
        : L("Tracking started – use your PC and check back.");

    /// All apps seen in the stored history, most used (last 7 days) first – for Settings.
    public List<AppUsage> KnownApps
    {
        get
        {
            var sums = new Dictionary<string, double>();
            for (int i = 0; i < 7; i++)
                foreach (var (id, s) in file.Days.GetValueOrDefault(DayKey(DateTime.Now.AddDays(-i)))?.Apps ?? [])
                    sums[id] = sums.GetValueOrDefault(id) + s;
            foreach (var id in file.Names.Keys) sums.TryAdd(id, 0);
            return sums.OrderByDescending(s => s.Value).ThenBy(s => Name(s.Key))
                       .Select((s, i) => new AppUsage(s.Key, Name(s.Key), s.Value, Category(s.Key), Brush(Category(s.Key)), 0, i + 1)).ToList();
        }
    }

    public string Name(string id) => file.Names.GetValueOrDefault(id) ?? Path.GetFileNameWithoutExtension(id);
    public bool IsExcluded(string id) => Excluded.Contains(id);

    public AppCategory Category(string id)
    {
        if (Overrides.TryGetValue(id, out var manual) && Enum.TryParse<AppCategory>(manual, true, out var c)) return c;
        if (!detected.TryGetValue(id, out var d)) detected[id] = d = Detect(id);
        return d;
    }

    public IBrush Brush(AppCategory category) =>
        new SolidColorBrush(Color.Parse(Colors.GetValueOrDefault(category.ToString().ToLowerInvariant()) ?? DefaultColor(category)));

    // MARK: Settings actions

    public void SetCategory(string id, AppCategory category)
    {
        var o = Overrides; o[id] = category.ToString().ToLowerInvariant();
        Prefs.Set("screentime.categoryOverrides", o);
        ChangedAll();
    }

    public void SetColor(AppCategory category, string hex)
    {
        var c = Colors; c[category.ToString().ToLowerInvariant()] = hex;
        Prefs.Set("screentime.colors", c);
        ChangedAll();
    }

    public void ToggleExcluded(string id)
    {
        var e = Excluded;
        if (!e.Remove(id)) e.Add(id);
        Prefs.Set("screentime.excluded", e);
        ChangedAll();
    }

    public void ResetToday()
    {
        file.Days[DayKey(DateTime.Now)] = new DayUsage();
        Save();
        ChangedAll();
    }

    // MARK: Categories

    public static string Title(AppCategory c) => c switch
    {
        AppCategory.Productivity => L("Productivity"), AppCategory.Development => L("Development"),
        AppCategory.Communication => L("Communication"), AppCategory.Browsing => L("Browsing"),
        AppCategory.Entertainment => L("Entertainment"), AppCategory.Creativity => L("Creativity"),
        AppCategory.Utilities => L("Utilities"), _ => L("Other"),
    };

    public static string DefaultColor(AppCategory c) => c switch
    {
        AppCategory.Productivity => "#5B9DFF", AppCategory.Development => "#B48CFF", AppCategory.Communication => "#4CD98C",
        AppCategory.Browsing => "#FFB84D", AppCategory.Entertainment => "#FF6B8A", AppCategory.Creativity => "#FF8A5B",
        AppCategory.Utilities => "#7FD3E6", _ => "#9A9AA5",
    };

    public static readonly string[] Swatches = ["#5B9DFF", "#B48CFF", "#4CD98C", "#FFB84D", "#FF6B8A", "#FF8A5B", "#7FD3E6", "#F2D16B", "#E07BE0", "#9A9AA5"];

    /// By executable name (Windows has no app-category metadata like the Mac's LSApplicationCategoryType).
    static AppCategory Detect(string exe)
    {
        var name = Path.GetFileNameWithoutExtension(exe);
        string[] browsers = ["chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc", "iexplore", "zen", "librewolf"];
        string[] communication = ["outlook", "olk", "ms-teams", "teams", "slack", "zoom", "telegram", "whatsapp", "discord", "signal", "skype", "thunderbird", "mailspring"];
        string[] development = ["code", "devenv", "windowsterminal", "cmd", "powershell", "pwsh", "wt", "rider64", "idea64", "pycharm64",
                                "webstorm64", "phpstorm64", "clion64", "goland64", "datagrip64", "sublime_text", "notepad++", "claude", "cursor", "windsurf", "docker desktop", "postman", "githubdesktop", "wsl", "bash", "mintty"];
        string[] productivity = ["winword", "excel", "powerpnt", "onenote", "msaccess", "notion", "obsidian", "acrobat", "acrord32", "evernote", "todoist", "notepad", "wordpad", "soffice", "loffice"];
        string[] entertainment = ["spotify", "vlc", "netflix", "steam", "epicgameslauncher", "music.ui", "video.ui", "mpc-hc64", "potplayermini64", "itunes", "tidal", "deezer"];
        string[] creativity = ["photoshop", "illustrator", "figma", "blender", "afterfx", "premiere pro", "indesign", "lightroom", "gimp-2.10", "inkscape", "krita", "davinci resolve", "resolve", "obs64", "affinity photo 2", "affinity designer 2", "paint", "mspaint"];
        string[] utilities = ["explorer", "taskmgr", "systemsettings", "control", "calc", "calculatorapp", "snippingtool", "7zfm", "everything", "powertoys"];
        if (browsers.Contains(name)) return AppCategory.Browsing;
        if (communication.Contains(name)) return AppCategory.Communication;
        if (development.Contains(name)) return AppCategory.Development;
        if (productivity.Contains(name)) return AppCategory.Productivity;
        if (entertainment.Contains(name)) return AppCategory.Entertainment;
        if (creativity.Contains(name)) return AppCategory.Creativity;
        if (utilities.Contains(name)) return AppCategory.Utilities;
        return AppCategory.Other;
    }
}
