using System.Text;
using System.Text.Json;
using Avalonia.Media;
using Avalonia.Threading;
using static HaloNotch.Loc;

namespace HaloNotch;

/// Token usage of Claude Code and Codex, read from their local session logs (%USERPROFILE%\.claude\projects,
/// %USERPROFILE%\.codex\sessions). Opt-in (the logs contain code and secrets); only usage numbers are read.
/// Port of the Mac app's UsageStore (incremental scan, Claude message dedupe, Codex cumulative deltas).
public sealed class UsageStore : Observable
{
    public record struct Totals(long Input, long CacheWrite, long Output, long CacheRead)
    {
        /// "Fresh": everything except context re-read from the cache.
        public readonly long Fresh => Input + CacheWrite + Output;
        public readonly long Processed => Fresh + CacheRead;
        public static Totals operator +(Totals a, Totals b) =>
            new(a.Input + b.Input, a.CacheWrite + b.CacheWrite, a.Output + b.Output, a.CacheRead + b.CacheRead);
    }

    public sealed record Limit(double UsedPercent, int WindowMinutes, DateTime ResetsAt)
    {
        public string ResetsText => LF("resets in %@", Fmt.Duration(Math.Max((ResetsAt - DateTime.Now).TotalSeconds, 0)));
    }

    public sealed record Tool(bool IsClaude, bool Installed, Totals Today, int Sessions, string? Model, string? Project, string? Plan, Limit? Primary, Limit? Secondary)
    {
        public string FreshText => Fmt.Tokens(Today.Fresh);
        public string InputText => Fmt.Tokens(Today.Input);
        public string CacheWriteText => Fmt.Tokens(Today.CacheWrite);
        public string OutputText => Fmt.Tokens(Today.Output);
        public string CacheReadText => Fmt.Tokens(Today.CacheRead);
        public string ProcessedText => Fmt.Tokens(Today.Processed);
        public string SessionsText => Sessions.ToString();
        public string Footer => string.Join(" · ", new[] { Model, Project }.Where(s => !string.IsNullOrEmpty(s)));
        public string PlanText => IsClaude ? L("Plan usage not available locally") : Plan ?? (Primary == null ? L("No plan data yet") : "");
    }

    public sealed record Cell(DateTime Day, int Level, IBrush Brush);

    static readonly IBrush[] LevelBrushes =
        [new SolidColorBrush(Color.Parse("#1AFFFFFF")), new SolidColorBrush(Color.Parse("#2E5C3D")), new SolidColorBrush(Color.Parse("#3E8E57")),
         new SolidColorBrush(Color.Parse("#4CC172")), new SolidColorBrush(Color.Parse("#6FE596"))];

    readonly Scanner scanner = new();
    DispatcherTimer? timer;
    bool scanning, loading = true;
    Tool claude = new(true, false, default, 0, null, null, null, null, null), codex = new(false, false, default, 0, null, null, null, null, null);
    Dictionary<DateTime, long> dailyClaude = [], dailyCodex = [];
    string source = "all";

    public bool Enabled
    {
        get => Prefs.Get(Prefs.UsageEnabled, false);
        set { Prefs.Set(Prefs.UsageEnabled, value); ChangedAll(); if (value) Activate(); }
    }

    public bool Disabled => !Enabled;
    public bool IsLoading => Enabled && loading;
    public bool Ready => Enabled && !loading;
    public bool NotFound => Ready && !claude.Installed && !codex.Installed;
    public Tool Claude => claude;
    public Tool Codex => codex;
    public string Source { get => source; set { source = value; ChangedAll(); } }

    const int Weeks = 18;

    /// Activity heatmap: one column per week, Monday on top, the current week last.
    public List<Cell> Heatmap
    {
        get
        {
            var daily = source == "claude" ? dailyClaude : source == "codex" ? dailyCodex
                : dailyClaude.Concat(dailyCodex).GroupBy(d => d.Key).ToDictionary(g => g.Key, g => g.Sum(d => d.Value));
            var today = DateTime.Today;
            var start = today.AddDays(-(((int)today.DayOfWeek + 6) % 7) - 7 * (Weeks - 1));
            long max = daily.Where(d => d.Key >= start).Select(d => d.Value).DefaultIfEmpty(0).Max();
            var cells = new List<Cell>();
            for (int i = 0; i < Weeks * 7; i++)
            {
                var day = start.AddDays(i);
                long v = day > today ? 0 : daily.GetValueOrDefault(day);
                int level = v <= 0 || max == 0 ? 0 : 1 + (int)Math.Min(3, Math.Floor(4.0 * v / (max + 1)));
                cells.Add(new Cell(day, level, day > today ? Brushes.Transparent : LevelBrushes[level]));
            }
            return cells;
        }
    }

    public string ActiveDays =>
        LF("%d active days", (source == "claude" ? dailyClaude : source == "codex" ? dailyCodex : dailyClaude.Concat(dailyCodex).GroupBy(d => d.Key).ToDictionary(g => g.Key, g => 1L))
            .Count(d => d.Key >= DateTime.Today.AddDays(-7 * Weeks) && d.Value > 0));

    /// Starts on first use of the Usage tab – the first scan reads every log once (can be hundreds of MB).
    public void Activate()
    {
        Refresh();
        if (timer != null) return;
        timer = new DispatcherTimer(TimeSpan.FromMinutes(2), DispatcherPriority.Background, (_, _) => Refresh());
        timer.Start();
    }

    public async void Refresh()
    {
        if (scanning || !Enabled) return;
        scanning = true;
        var result = await Task.Run(scanner.Scan);
        (claude, codex, dailyClaude, dailyCodex) = result;
        loading = false;
        scanning = false;
        ChangedAll();
    }

    // MARK: Scanner (background, incremental)

    sealed class Scanner
    {
        static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        static readonly string ClaudeRoot = Path.Combine(Home, ".claude", "projects");
        static readonly string CodexRoot = Path.Combine(Home, ".codex", "sessions");
        static readonly byte[] UsageMarker = Encoding.UTF8.GetBytes("\"usage\"");
        static readonly byte[] TokenCountMarker = Encoding.UTF8.GetBytes("\"token_count\"");
        static readonly byte[] TurnContextMarker = Encoding.UTF8.GetBytes("\"turn_context\"");

        readonly Dictionary<string, long> offsets = [];
        readonly HashSet<string> seenClaude = [];
        readonly Dictionary<DateTime, Totals> claudeDays = [], codexDays = [];
        readonly Dictionary<DateTime, HashSet<string>> claudeSessions = [], codexSessions = [];
        readonly Dictionary<string, Totals> codexPrev = [];
        (DateTime Date, string? Model, string? Project)? claudeLast, codexLast;
        (DateTime Date, string? Plan, Limit? Primary, Limit? Secondary)? codexLimits;

        public (Tool, Tool, Dictionary<DateTime, long>, Dictionary<DateTime, long>) Scan()
        {
            bool claudeInstalled = Directory.Exists(ClaudeRoot), codexInstalled = Directory.Exists(CodexRoot);
            if (claudeInstalled) foreach (var f in Files(ClaudeRoot)) Read(f, () => { }, ClaudeLine);
            if (codexInstalled) foreach (var f in Files(CodexRoot)) Read(f, () => codexPrev.Remove(f), line => CodexLine(line, f));
            var today = DateTime.Today;
            var c = new Tool(true, claudeInstalled, claudeDays.GetValueOrDefault(today), claudeSessions.GetValueOrDefault(today)?.Count ?? 0,
                             claudeLast?.Model, claudeLast?.Project, null, null, null);
            var x = new Tool(false, codexInstalled, codexDays.GetValueOrDefault(today), codexSessions.GetValueOrDefault(today)?.Count ?? 0,
                             codexLast?.Model, codexLast?.Project, codexLimits?.Plan, codexLimits?.Primary, codexLimits?.Secondary);
            return (c, x, claudeDays.ToDictionary(d => d.Key, d => d.Value.Fresh), codexDays.ToDictionary(d => d.Key, d => d.Value.Fresh));
        }

        static IEnumerable<string> Files(string root)
        {
            try { return Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories).ToList(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
        }

        /// Reads the complete lines appended since the last scan.
        void Read(string path, Action onReset, Action<byte[]> handle)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                long offset = offsets.GetValueOrDefault(path);
                if (fs.Length < offset) { offset = 0; onReset(); }       // file was rewritten
                if (fs.Length <= offset) return;
                fs.Seek(offset, SeekOrigin.Begin);
                var pending = new List<byte>();
                var chunk = new byte[1 << 20];
                int read;
                while ((read = fs.Read(chunk, 0, chunk.Length)) > 0)
                {
                    int start = 0;
                    for (int i = 0; i < read; i++)
                    {
                        if (chunk[i] != (byte)'\n') continue;
                        pending.AddRange(new ArraySegment<byte>(chunk, start, i - start));
                        offset += pending.Count + 1;
                        handle(pending.ToArray());
                        pending.Clear();
                        start = i + 1;
                    }
                    pending.AddRange(new ArraySegment<byte>(chunk, start, read - start));   // unfinished last line waits for next time
                    if (pending.Count > 16 << 20) { offset = fs.Length; break; }              // no log line – skip the file
                }
                offsets[path] = offset;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }

        static DateTime? Date(JsonElement o, string key) =>
            o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(v.GetString(), out var d) ? d.LocalDateTime : null;
        static long Long(JsonElement o, string key) => o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;
        static string? Str(JsonElement o, string key) => o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        static string? Folder(string? cwd) => cwd == null ? null : Path.GetFileName(cwd.TrimEnd('/', '\\'));

        void ClaudeLine(byte[] line)
        {
            if (line.AsSpan().IndexOf(UsageMarker) < 0) return;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var obj = doc.RootElement;
                if (!obj.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object
                    || !message.TryGetProperty("usage", out var usage) || Date(obj, "timestamp") is not { } when) return;
                // The same message is logged once per content block → dedupe.
                if (!seenClaude.Add($"{Str(message, "id") ?? Str(obj, "uuid")}|{Str(obj, "requestId")}")) return;
                var t = new Totals(Long(usage, "input_tokens"), Long(usage, "cache_creation_input_tokens"),
                                   Long(usage, "output_tokens"), Long(usage, "cache_read_input_tokens"));
                var day = when.Date;
                claudeDays[day] = claudeDays.GetValueOrDefault(day) + t;
                if (Str(obj, "sessionId") is { } session) { if (!claudeSessions.TryGetValue(day, out var s)) claudeSessions[day] = s = []; s.Add(session); }
                var model = Str(message, "model");
                if ((claudeLast == null || when >= claudeLast.Value.Date) && model != "<synthetic>")
                    claudeLast = (when, model, Folder(Str(obj, "cwd")));
            }
            catch (JsonException) { }
        }

        void CodexLine(byte[] line, string session)
        {
            bool isTokens = line.AsSpan().IndexOf(TokenCountMarker) >= 0;
            bool isContext = !isTokens && line.AsSpan().IndexOf(TurnContextMarker) >= 0;
            if (!isTokens && !isContext) return;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var obj = doc.RootElement;
                if (!obj.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object || Date(obj, "timestamp") is not { } when) return;
                if (isContext)
                {
                    if (Str(obj, "type") == "turn_context" && (codexLast == null || when >= codexLast.Value.Date))
                        codexLast = (when, Str(payload, "model"), Folder(Str(payload, "cwd")));
                    return;
                }
                if (Str(payload, "type") != "token_count") return;
                if (payload.TryGetProperty("info", out var info) && info.ValueKind == JsonValueKind.Object
                    && info.TryGetProperty("total_token_usage", out var total))
                {
                    long input = Long(total, "input_tokens"), cached = Long(total, "cached_input_tokens");
                    // token_count carries cumulative totals per session → count the deltas.
                    var now = new Totals(Math.Max(0, input - cached), Long(total, "cache_write_input_tokens"), Long(total, "output_tokens"), cached);
                    var prev = codexPrev.GetValueOrDefault(session);
                    var delta = now.Processed < prev.Processed ? now
                        : new Totals(Math.Max(0, now.Input - prev.Input), Math.Max(0, now.CacheWrite - prev.CacheWrite),
                                     Math.Max(0, now.Output - prev.Output), Math.Max(0, now.CacheRead - prev.CacheRead));
                    codexPrev[session] = now;
                    if (delta.Processed > 0)
                    {
                        var day = when.Date;
                        codexDays[day] = codexDays.GetValueOrDefault(day) + delta;
                        if (!codexSessions.TryGetValue(day, out var s)) codexSessions[day] = s = [];
                        s.Add(session);
                    }
                }
                if (payload.TryGetProperty("rate_limits", out var limits) && limits.ValueKind == JsonValueKind.Object
                    && (codexLimits == null || when >= codexLimits.Value.Date))
                {
                    Limit? Read(string key) => limits.TryGetProperty(key, out var l) && l.ValueKind == JsonValueKind.Object
                        && l.TryGetProperty("used_percent", out var used) && used.ValueKind == JsonValueKind.Number
                        && l.TryGetProperty("resets_at", out var resets) && resets.ValueKind == JsonValueKind.Number
                        ? new Limit(used.GetDouble(), (int)Long(l, "window_minutes"), DateTimeOffset.FromUnixTimeSeconds((long)resets.GetDouble()).LocalDateTime)
                        : null;
                    codexLimits = (when, Str(limits, "plan_type"), Read("primary"), Read("secondary"));
                }
            }
            catch (JsonException) { }
        }
    }
}
