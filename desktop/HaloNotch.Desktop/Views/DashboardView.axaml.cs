using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using static HaloNotch.Loc;

namespace HaloNotch.Views;

public enum DashboardSection { UpNext, NowPlaying, Focus, Weather, Clipboard, ScreenTime, DayProgress, QuickControls }

/// Dashboard: up to 8 sections in two rows with hairline dividers (Settings → Dashboard hides sections).
public partial class DashboardView : UserControl
{
    public event Action? OpenSettings;
    readonly DispatcherTimer minuteTimer;

    public static string Title(DashboardSection s) => s switch
    {
        DashboardSection.UpNext => L("Up Next"), DashboardSection.NowPlaying => L("Now Playing"), DashboardSection.Focus => L("Focus"),
        DashboardSection.Weather => L("Weather"), DashboardSection.Clipboard => L("Clipboard"), DashboardSection.ScreenTime => L("Screen Time"),
        DashboardSection.DayProgress => L("Day Progress"), _ => L("Quick controls"),
    };

    /// Hidden sections (Focus is hidden by default, like on the Mac).
    public static HashSet<DashboardSection> Hidden
    {
        get => Prefs.Get(Prefs.DashboardHidden, new List<string> { "focus" })
                    .Select(s => Enum.TryParse<DashboardSection>(s, true, out var v) ? v : (DashboardSection?)null).OfType<DashboardSection>().ToHashSet();
        set => Prefs.Set(Prefs.DashboardHidden, value.Select(s => char.ToLowerInvariant(s.ToString()[0]) + s.ToString()[1..]).OrderBy(s => s).ToList());
    }

    public DashboardView()
    {
        InitializeComponent();
        UpNextTitle.Text = L("Up Next"); NowPlayingTitle.Text = L("Now Playing"); FocusTitle.Text = L("Focus");
        WeatherTitle.Text = L("Weather"); ClipboardTitle.Text = L("Clipboard"); ScreenTimeTitle.Text = L("Screen Time");
        DayTitle.Text = L("Day Progress"); ControlsTitle.Text = L("Quick controls");
        UpNextHint.Text = L("See your next events here.");
        AddCalendarButton.Content = L("Add calendar");
        NothingPlanned.Text = L("Nothing planned");
        CopySomething.Text = L("Copy something");
        AllHidden.Text = L("All sections are hidden. Turn them on in Settings → Dashboard.");
        Hub.Clipboard.PropertyChanged += (_, _) => UpdateClipboard();
        Hub.Focus.PropertyChanged += (_, _) => UpdateDay();
        UpdateClipboard();
        Arrange();
        minuteTimer = new DispatcherTimer(TimeSpan.FromSeconds(60), DispatcherPriority.Background, (_, _) => UpdateDay());
        AttachedToVisualTree += (_, _) => { UpdateDay(); minuteTimer.Start(); Arrange(); };
        DetachedFromVisualTree += (_, _) => minuteTimer.Stop();
    }

    /// Visible sections fill the top row first (up to 4), the rest goes below.
    public void Arrange()
    {
        var hidden = Hidden;
        var all = new Dictionary<DashboardSection, Control>
        {
            [DashboardSection.UpNext] = UpNext, [DashboardSection.NowPlaying] = NowPlaying, [DashboardSection.Focus] = FocusSection,
            [DashboardSection.Weather] = Weather, [DashboardSection.Clipboard] = Clipboard, [DashboardSection.ScreenTime] = ScreenTime,
            [DashboardSection.DayProgress] = DayProgress, [DashboardSection.QuickControls] = QuickControls,
        };
        foreach (var c in all.Values) (c.Parent as Panel)?.Children.Remove(c);
        TopRow.Children.Clear(); BottomRow.Children.Clear();
        TopRow.ColumnDefinitions.Clear(); BottomRow.ColumnDefinitions.Clear();
        var visible = Enum.GetValues<DashboardSection>().Where(s => !hidden.Contains(s)).Select(s => all[s]).ToList();
        foreach (var c in all.Values.Except(visible)) Pool.Children.Add(c);
        var top = visible.Take(4).ToList();
        var bottom = visible.Skip(4).ToList();
        Fill(TopRow, top);
        Fill(BottomRow, bottom);
        Rows.RowDefinitions[2].Height = bottom.Count > 0 ? GridLength.Star : new GridLength(0);
        Rows.RowDefinitions[1].Height = bottom.Count > 0 ? GridLength.Auto : new GridLength(0);
        AllHidden.IsVisible = visible.Count == 0;
    }

    static void Fill(Grid row, List<Control> sections)
    {
        for (int i = 0; i < sections.Count; i++)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            Grid.SetColumn(sections[i], i);
            row.Children.Add(sections[i]);
            if (i > 0)
            {
                var line = new Border { Classes = { "vline" }, HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 6) };
                Grid.SetColumn(line, i);
                row.Children.Add(line);
            }
        }
    }

    void UpdateClipboard()
    {
        ClipCount.Text = $"{Hub.Clipboard.Count}/50";
        CopySomething.IsVisible = Hub.Clipboard.Count == 0;
    }

    /// Work day 9:00 – 18:00 (same as the Mac app), refreshed every minute.
    void UpdateDay()
    {
        var now = DateTime.Now;
        DateTime start = now.Date.AddHours(9), end = now.Date.AddHours(18);
        double progress = Math.Clamp((now - start).TotalSeconds / (end - start).TotalSeconds, 0, 1);
        DayLeft.Text = progress >= 1 ? L("Day done") : progress <= 0 ? L("Not started") : LF("%@ left", Fmt.Duration((end - now).TotalSeconds));
        DayBar.Value = progress;
        DayFooter.Text = LF("Sessions today: %d", Hub.Focus.SessionsToday) + " · " + L("Eyes need rest too.");
    }

    void OnSettings(object? s, RoutedEventArgs e) => OpenSettings?.Invoke();
    void OnJoin(object? s, RoutedEventArgs e) { if ((s as Control)?.DataContext is CalendarEntry entry) Hub.Calendar.Join(entry); }
    void OnPrevious(object? s, RoutedEventArgs e) => Hub.Media.Previous();
    void OnPlayPause(object? s, RoutedEventArgs e) => Hub.Media.PlayPause();
    void OnNext(object? s, RoutedEventArgs e) => Hub.Media.Next();
    void OnFocusToggle(object? s, RoutedEventArgs e) => Hub.Focus.Toggle();
    void OnMute(object? s, RoutedEventArgs e) => Hub.Controls.ToggleMute();
    void OnClipCopy(object? s, RoutedEventArgs e) { if ((s as Control)?.DataContext is ClipItem item) Hub.Clipboard.Copy(item); }
}
