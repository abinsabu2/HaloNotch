using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text.RegularExpressions;
using Avalonia.Media;
using Avalonia.Threading;
using Calendar = Ical.Net.Calendar;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using static HaloNotch.Loc;

namespace HaloNotch;

public sealed record CalendarEntry(string Title, DateTime Start, DateTime End, bool IsAllDay, IBrush Color, string? JoinUrl)
{
    public string TimeText => IsAllDay ? L("All-day") : Fmt.Time(Start);
    public string RangeText => IsAllDay ? L("All day") : $"{Fmt.Time(Start)} – {Fmt.Time(End)}";
    public string DayText => Start.Date == DateTime.Today ? L("Today") : Start.Date == DateTime.Today.AddDays(1) ? L("Tomorrow")
                             : Start.ToString("ddd d. MMM", CultureInfo.CurrentCulture);
    public bool CanJoin => JoinUrl != null;
}

/// Calendar events from ICS links (Outlook.com / Microsoft 365, Google, iCloud "secret address").
/// Windows offers no shared calendar store to unpackaged apps (the Mac app uses EventKit).
/// ponytail: read-only, refreshed every 15 min; upgrade path is Microsoft Graph with sign-in.
public sealed class CalendarStore : Observable
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    static readonly string[] Palette = ["#5B9DFF", "#FF8A5B", "#4CD98C", "#B48CFF", "#FFB84D", "#FF6B8A"];

    List<(Calendar Calendar, IBrush Color)> calendars = [];
    DateTime selected = DateTime.Today, lastLoad = DateTime.MinValue;
    string status = "";
    DispatcherTimer? timer;

    public List<string> Feeds => Prefs.Get(Prefs.CalendarFeeds, new List<string>());
    public bool HasFeeds => Feeds.Count > 0;
    public bool NoFeeds => !HasFeeds;
    public string Status => status;

    public List<CalendarEntry> Upcoming { get; private set; } = [];
    public List<CalendarEntry> DayEntries { get; private set; } = [];
    public HashSet<DateTime> EventDays { get; private set; } = [];
    public DateTime Selected => selected;

    public bool NoUpcoming => Upcoming.Count == 0;
    public bool NoDayEntries => DayEntries.Count == 0;
    public CalendarEntry? Next => Upcoming.FirstOrDefault();
    public string MonthTitle => selected.ToString("MMMM", CultureInfo.CurrentCulture);
    public string YearTitle => selected.Year.ToString(CultureInfo.CurrentCulture);
    public string DayTitle => selected.ToString("D", CultureInfo.CurrentCulture);
    public string DayCaption => (selected.ToString("dddd", CultureInfo.CurrentCulture) + (selected == DateTime.Today ? " · " + L("Today") : "")).ToUpperInvariant();

    public void Start()
    {
        _ = Reload();
        timer = new DispatcherTimer(TimeSpan.FromMinutes(15), DispatcherPriority.Background, (_, _) => _ = Reload());
        timer.Start();
    }

    /// Called when the panel opens: refresh the lists (cheap) and the feeds if older than 5 minutes.
    public void RefreshIfStale()
    {
        if ((DateTime.Now - lastLoad).TotalMinutes > 5) _ = Reload(); else Publish();
    }

    public void AddFeed(string url)
    {
        url = url.Trim();
        if (url.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)) url = "https://" + url[9..];
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) { status = L("Use an https:// or webcal:// calendar link"); ChangedAll(); return; }
        var feeds = Feeds;
        if (!feeds.Contains(url)) feeds.Add(url);
        Prefs.Set(Prefs.CalendarFeeds, feeds);
        _ = Reload();
    }

    public void RemoveFeed(string url)
    {
        Prefs.Set(Prefs.CalendarFeeds, Feeds.Where(f => f != url).ToList());
        _ = Reload();
    }

    public async Task Reload()
    {
        var loaded = new List<(Calendar, IBrush)>();
        var feeds = Feeds;
        string error = "";
        for (int i = 0; i < feeds.Count; i++)
        {
            try
            {
                var text = await Http.GetStringAsync(feeds[i]);
                var calendar = await Task.Run(() => Calendar.Load(text));
                if (calendar != null) loaded.Add((calendar, new SolidColorBrush(Color.Parse(Palette[i % Palette.Length]))));
            }
            catch (Exception e)
            {
                Debug.WriteLine($"HaloNotch: calendar feed failed – {e.Message}");
                error = LF("Couldn't load %@", new Uri(feeds[i]).Host);
            }
        }
        calendars = loaded;
        lastLoad = DateTime.Now;
        status = error;
        Publish();
    }

    void Publish()
    {
        var now = DateTime.Now;
        Upcoming = Events(now, DateTime.Today.AddDays(2)).Where(e => e.End > now).Take(8).ToList();
        DayEntries = Events(selected, selected.AddDays(1)).ToList();
        var monthStart = new DateTime(selected.Year, selected.Month, 1);
        var days = new HashSet<DateTime>();
        foreach (var e in Events(monthStart, monthStart.AddMonths(1)))
            for (var d = (e.Start < monthStart ? monthStart : e.Start).Date; d < e.End && d < monthStart.AddMonths(1); d = d.AddDays(1)) days.Add(d);
        EventDays = days;
        ChangedAll();
    }

    /// Occurrences (recurrences expanded) overlapping [from, to), sorted by start, in local time.
    IEnumerable<CalendarEntry> Events(DateTime from, DateTime to)
    {
        var list = new List<CalendarEntry>();
        foreach (var (calendar, color) in calendars)
        {
            try
            {
                // Start a day early so multi-day / all-day events that began before `from` are included.
                foreach (var o in calendar.GetOccurrences<CalendarEvent>(new CalDateTime(from.AddDays(-1).ToUniversalTime(), "UTC"))
                                          .TakeWhile(o => Local(o.Period.StartTime) < to))     // sorted by start
                {
                    if (o.Source is not CalendarEvent ev) continue;
                    bool allDay = !o.Period.StartTime.HasTime;
                    var start = Local(o.Period.StartTime);
                    var end = o.Period.EffectiveEndTime is { } e ? Local(e) : start.AddHours(allDay ? 24 : 1);
                    if (end <= from || start >= to) continue;
                    list.Add(new CalendarEntry(string.IsNullOrWhiteSpace(ev.Summary) ? L("Untitled") : ev.Summary, start, end, allDay, color,
                        MeetingLink(ev.Url?.ToString(), ev.Location, ev.Description)));
                }
            }
            catch (Exception e) { Debug.WriteLine($"HaloNotch: calendar evaluation failed – {e.Message}"); }
        }
        return list.OrderBy(e => e.Start);
    }

    static DateTime Local(CalDateTime t) => !t.HasTime ? t.Value.Date : t.AsUtc.ToLocalTime();

    // MARK: Month grid / day selection

    public void Select(DateTime day) { selected = day.Date; Publish(); }
    public void ShiftMonth(int months) => Select(selected.AddMonths(months));
    public void ShiftYear(int years) => Select(selected.AddYears(years));
    public void GoToToday() => Select(DateTime.Today);

    /// Month grid cells: leading blanks (null) so the 1st falls on the right weekday column.
    public List<DateTime?> GridCells
    {
        get
        {
            var first = new DateTime(selected.Year, selected.Month, 1);
            int blanks = ((int)first.DayOfWeek - (int)CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek + 7) % 7;
            var cells = Enumerable.Repeat<DateTime?>(null, blanks).ToList();
            for (int d = 0; d < DateTime.DaysInMonth(selected.Year, selected.Month); d++) cells.Add(first.AddDays(d));
            return cells;
        }
    }

    /// Very short weekday symbols ordered by the user's first weekday (M T W T F S S).
    public static List<string> WeekdaySymbols
    {
        get
        {
            var f = CultureInfo.CurrentCulture.DateTimeFormat;
            return Enumerable.Range(0, 7).Select(i => f.ShortestDayNames[((int)f.FirstDayOfWeek + i) % 7][..1].ToUpperInvariant()).ToList();
        }
    }

    // MARK: Meeting links

    static readonly string[] MeetingHosts = ["teams.microsoft.com", "teams.live.com", "zoom.us", "meet.google.com",
                                             "webex.com", "whereby.com", "gotomeeting.com", "meet.jit.si"];
    static readonly Regex UrlPattern = new(@"https://[^\s<>""']+", RegexOptions.IgnoreCase);

    /// First HTTPS link whose *host* is a known meeting service (invites can come from anyone –
    /// `https://evil.example/zoom.us` must not pass).
    public static string? MeetingLink(params string?[] fields)
    {
        foreach (var text in fields)
        {
            if (text == null) continue;
            foreach (Match m in UrlPattern.Matches(text))
            {
                if (!Uri.TryCreate(m.Value, UriKind.Absolute, out var uri)) continue;
                var host = uri.Host.ToLowerInvariant();
                if (MeetingHosts.Any(h => host == h || host.EndsWith("." + h))) return uri.ToString();
            }
        }
        return null;
    }

    public void Join(CalendarEntry entry) { if (entry.JoinUrl != null) Shell.Open(entry.JoinUrl); }

#if DEBUG
    /// Weekly recurring event expands; meeting links are matched by host only.
    public static void SelfCheck()
    {
        var monday = DateTime.Today.AddDays(-(((int)DateTime.Today.DayOfWeek + 6) % 7));
        var ics = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:test\r\nBEGIN:VEVENT\r\nUID:1\r\n" +
                  $"DTSTART:{monday.AddHours(10).ToUniversalTime():yyyyMMdd'T'HHmmss'Z'}\r\nDTEND:{monday.AddHours(11).ToUniversalTime():yyyyMMdd'T'HHmmss'Z'}\r\n" +
                  "RRULE:FREQ=WEEKLY;COUNT=10\r\nSUMMARY:Standup\r\nLOCATION:https://teams.microsoft.com/l/meetup-join/abc\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
        var store = new CalendarStore { calendars = [(Calendar.Load(ics)!, Brushes.Gray)] };
        var events = store.Events(monday, monday.AddDays(21)).ToList();
        Debug.Assert(events.Count == 3 && events[1].Start == monday.AddDays(7).AddHours(10), "weekly recurrence");
        Debug.Assert(events[0].JoinUrl != null && MeetingLink("https://evil.example/zoom.us") == null, "meeting links");
    }
#endif
}
