using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using HaloNotch.Views;
using static HaloNotch.Loc;

namespace HaloNotch;

/// Settings: General, Dashboard, Weather, Calendar, Screen Time (port of the Mac app's SettingsWindow).
public partial class SettingsWindow : Window
{
    readonly Action displaysChanged, dashboardChanged;

    public SettingsWindow() : this(() => { }, () => { }) { }

    public SettingsWindow(Action displaysChanged, Action dashboardChanged)
    {
        InitializeComponent();
        this.displaysChanged = displaysChanged;
        this.dashboardChanged = dashboardChanged;
        Title = L("HaloNotch Settings");
        GeneralTab.Header = L("General");
        DashboardTab.Header = L("Dashboard");
        WeatherTab.Header = L("Weather");
        CalendarTab.Header = L("Calendar");
        ScreenTimeTab.Header = L("Screen Time");
        BuildGeneral();
        BuildDashboard();
        BuildWeather();
        BuildCalendar();
        BuildScreenTime();
        Hub.Weather.PropertyChanged += (_, _) => UpdateWeather();
        Hub.Calendar.PropertyChanged += (_, _) => BuildFeedList();
        Hub.Reminders.PropertyChanged += (_, _) => BuildReminderList();
    }

    public void ShowTab(int index) => Tabs.SelectedIndex = index;

    // MARK: Helpers

    static TextBlock Section(string text) => new() { Text = text, Classes = { "section" } };
    static TextBlock Note(string text) => new() { Text = text, Classes = { "note" } };

    static CheckBox Check(string text, bool value, Action<bool> changed)
    {
        var box = new CheckBox { Content = text, IsChecked = value };
        box.IsCheckedChanged += (_, _) => changed(box.IsChecked == true);
        return box;
    }

    static CheckBox Pref(string text, string key, bool fallback = true, Action? changed = null) =>
        Check(text, Prefs.Get(key, fallback), v => { Prefs.Set(key, v); changed?.Invoke(); });

    static Button Action(string text, Action click, bool pill = false)
    {
        var b = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Left };
        if (pill) b.Classes.Add("pill");
        b.Click += (_, _) => click();
        return b;
    }

    static StackPanel Row(params Control[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        foreach (var c in children) { c.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(c); }
        return row;
    }

    // MARK: General

    void BuildGeneral()
    {
        var p = GeneralPage.Children;
        p.Add(Section(L("Language")));
        var language = new ComboBox { ItemsSource = Loc.Languages.Select(l => l.Code == "system" ? L("System") : l.Name).ToList(), Width = 240 };
        var current = Prefs.Get(Prefs.Language, "system");
        language.SelectedIndex = Math.Max(0, Array.FindIndex(Loc.Languages, l => l.Code == current));
        language.SelectionChanged += (_, _) => { if (language.SelectedIndex >= 0) Prefs.Set(Prefs.Language, Loc.Languages[language.SelectedIndex].Code); };
        p.Add(Row(new TextBlock { Text = L("App language") }, language));
        p.Add(Note(L("The new language is used after relaunching.")));
        p.Add(Action(L("Relaunch HaloNotch"), Relaunch));

        p.Add(Section(L("Displays")));
        var displays = new ComboBox { ItemsSource = new[] { L("All displays"), L("Main display only") }, Width = 240 };
        displays.SelectedIndex = Prefs.Get(Prefs.DisplayMode, "all") == "main" ? 1 : 0;
        displays.SelectionChanged += (_, _) => { Prefs.Set(Prefs.DisplayMode, displays.SelectedIndex == 1 ? "main" : "all"); displaysChanged(); };
        p.Add(Row(new TextBlock { Text = L("Show notch on") }, displays));
#if WINDOWS
        p.Add(Check(L("Start with Windows"), Win.Startup.Enabled, v => Win.Startup.Enabled = v));
#endif

        p.Add(Section(L("Clipboard shortcut")));
        p.Add(Note(L("Ctrl+Alt+V opens the clipboard, keys 1–0 paste an item.")));
        p.Add(Pref(L("Paste directly with Ctrl+Alt+V"), Prefs.PasteDirectly));
        p.Add(Pref(L("Ignore Password Manager Copies (Enpass …)"), Prefs.IgnorePasswordManagers));

        p.Add(Section(L("Media")));
        p.Add(Pref(L("Real Equalizer (System Audio)"), Prefs.RealEqualizer));
        p.Add(Pref(L("Show Volume HUD"), Prefs.VolumeHUD));
        p.Add(Pref(L("Show Brightness HUD"), Prefs.BrightnessHUD));

        p.Add(Section(L("Usage")));
        p.Add(Check(L("Read Claude Code and Codex session logs"), Hub.Usage.Enabled, v => Hub.Usage.Enabled = v));

        p.Add(Section(L("About")));
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        p.Add(Note($"HaloNotch · {LF("Version %@", $"{version?.Major}.{version?.Minor}.{version?.Build}")}"));
        p.Add(Note(L("Forecast data: Open-Meteo.com (free for non-commercial use).")));
    }

    /// The language is fixed at launch – start a new instance (it waits for this one to exit).
    static void Relaunch()
    {
        if (Environment.ProcessPath is not { } exe) return;
        var args = exe.EndsWith("dotnet", StringComparison.OrdinalIgnoreCase) || exe.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase)
            ? $"\"{Environment.GetCommandLineArgs()[0]}\" --relaunch" : "--relaunch";     // dev: `dotnet HaloNotch.dll`
        Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false });
        (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    // MARK: Dashboard

    void BuildDashboard()
    {
        var p = DashboardPage.Children;
        p.Add(Section(L("Sections on the Dashboard")));
        foreach (var section in Enum.GetValues<DashboardSection>())
            p.Add(Check(DashboardView.Title(section), !DashboardView.Hidden.Contains(section), visible =>
            {
                var hidden = DashboardView.Hidden;
                if (visible) hidden.Remove(section); else hidden.Add(section);
                DashboardView.Hidden = hidden;
                dashboardChanged();
            }));
        p.Add(Note(L("Hidden sections make room for the others.")));
    }

    // MARK: Weather

    RadioButton? celsius, fahrenheit, automatic, byCity;
    TextBox? city;
    TextBlock? place, weatherStatus;
    StackPanel? cityRow;
    Button? locationButton;

    void BuildWeather()
    {
        var w = Hub.Weather;
        var p = WeatherPage.Children;
        p.Add(Section(L("Units")));
        celsius = new RadioButton { Content = "°C", GroupName = "unit", IsChecked = w.Celsius };
        fahrenheit = new RadioButton { Content = "°F", GroupName = "unit", IsChecked = !w.Celsius };
        celsius.IsCheckedChanged += (_, _) => { if (celsius.IsChecked == true && !w.Celsius) w.Celsius = true; };
        fahrenheit.IsCheckedChanged += (_, _) => { if (fahrenheit.IsChecked == true && w.Celsius) w.Celsius = false; };
        p.Add(Row(celsius, fahrenheit));

        p.Add(Section(L("Location")));
        automatic = new RadioButton { Content = L("Use my location"), GroupName = "location", IsChecked = w.Automatic };
        byCity = new RadioButton { Content = L("Choose a city"), GroupName = "location", IsChecked = !w.Automatic };
        automatic.IsCheckedChanged += (_, _) => { if (automatic.IsChecked == true && !w.Automatic) w.Automatic = true; UpdateWeather(); };
        byCity.IsCheckedChanged += (_, _) => { if (byCity.IsChecked == true && w.Automatic) w.Automatic = false; UpdateWeather(); };
        p.Add(automatic);
        p.Add(byCity);
        city = new TextBox { Text = w.CityQuery, PlaceholderText = L("e.g. Braunschweig"), Width = 260 };
        city.TextChanged += (_, _) => w.CityQuery = city.Text ?? "";
        city.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) w.ApplyCity(); };
        cityRow = Row(city, Action(L("Apply"), w.ApplyCity));
        p.Add(cityRow);
        locationButton = Action(L("Open Location Privacy Settings…"), w.OpenLocationSettings);
        p.Add(locationButton);
        place = new TextBlock();
        p.Add(Row(new TextBlock { Text = L("Current place"), Foreground = Brushes.Gray }, place));
        weatherStatus = new TextBlock { Foreground = new SolidColorBrush(Color.Parse("#FF6B6B")), FontSize = 12, TextWrapping = TextWrapping.Wrap };
        p.Add(weatherStatus);
        p.Add(Note(L("Forecast data: Open-Meteo.com (free for non-commercial use).")));
        UpdateWeather();
    }

    void UpdateWeather()
    {
        if (cityRow == null) return;
        cityRow.IsVisible = !Hub.Weather.Automatic;
        locationButton!.IsVisible = Hub.Weather.Automatic;
        place!.Text = Hub.Weather.PlaceName;
        weatherStatus!.Text = Hub.Weather.Status;
    }

    // MARK: Calendar

    StackPanel? feedList, reminderList;

    void BuildCalendar()
    {
        var p = CalendarPage.Children;
        p.Add(Section(L("Calendar links (ICS)")));
        p.Add(Note(L("Paste the private iCal / ICS address of a calendar. Outlook: Settings → Calendar → Shared calendars → Publish a calendar → ICS link. Google: calendar settings → Secret address in iCal format. iCloud: share calendar → Public calendar.")));
        var input = new TextBox { PlaceholderText = "https://…/calendar.ics", Width = 440 };
        void Add() { Hub.Calendar.AddFeed(input.Text ?? ""); input.Text = ""; }
        input.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) Add(); };
        p.Add(Row(input, Action(L("Add"), Add)));
        feedList = new StackPanel { Spacing = 6 };
        p.Add(feedList);
        p.Add(Note(L("Links are stored only on this PC and refreshed every 15 minutes. Treat them like passwords – anyone with the link can read the calendar.")));
        BuildFeedList();

        p.Add(Section(L("Reminders")));
        p.Add(Note(L("Reminders are stored only on this PC. The notch opens when one is due.")));
        var title = new TextBox { PlaceholderText = L("New reminder"), Width = 440, HorizontalAlignment = HorizontalAlignment.Left };
        var next = DateTime.Today.AddHours(DateTime.Now.Hour + 1);
        var date = new CalendarDatePicker { SelectedDate = next.Date, Width = 150 };
        var time = new TimePicker
        {
            SelectedTime = next.TimeOfDay,
            ClockIdentifier = CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('H') ? "24HourClock" : "12HourClock",
        };
        void AddReminder()
        {
            if (date.SelectedDate is not { } d || time.SelectedTime is not { } t) return;
            Hub.Reminders.Add(title.Text ?? "", d.Date + t);
            title.Text = "";
        }
        title.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) AddReminder(); };
        p.Add(title);
        p.Add(Row(date, time, Action(L("Add"), AddReminder)));
        reminderList = new StackPanel { Spacing = 6 };
        p.Add(reminderList);
        BuildReminderList();
    }

    void BuildReminderList()
    {
        if (reminderList == null) return;
        reminderList.Children.Clear();
        foreach (var r in Hub.Reminders.Items)
            reminderList.Children.Add(Row(new TextBlock { Text = $"{Fmt.ShortDate(r.Due)} {Fmt.Time(r.Due)} · {r.Title}", Width = 380, TextTrimming = TextTrimming.CharacterEllipsis },
                                          Action(L("Remove"), () => Hub.Reminders.Remove(r))));
    }

    void BuildFeedList()
    {
        if (feedList == null) return;
        feedList.Children.Clear();
        foreach (var feed in Hub.Calendar.Feeds)
        {
            var host = Uri.TryCreate(feed, UriKind.Absolute, out var u) ? u.Host : feed;
            feedList.Children.Add(Row(new TextBlock { Text = host, Width = 380, TextTrimming = TextTrimming.CharacterEllipsis },
                                      Action(L("Remove"), () => Hub.Calendar.RemoveFeed(feed))));
        }
        if (Hub.Calendar.Status.Length > 0)
            feedList.Children.Add(new TextBlock { Text = Hub.Calendar.Status, Foreground = new SolidColorBrush(Color.Parse("#FF6B6B")), FontSize = 12 });
    }

    // MARK: Screen Time

    void BuildScreenTime()
    {
        var st = Hub.ScreenTime;
        var p = ScreenTimePage.Children;
        p.Add(Section(L("General")));
        var reset = new ComboBox { ItemsSource = Enumerable.Range(0, 7).Select(h => $"{h:00}:00").ToList(), SelectedIndex = st.ResetHour, Width = 120 };
        reset.SelectionChanged += (_, _) => { if (reset.SelectedIndex >= 0) st.ResetHour = reset.SelectedIndex; };
        p.Add(Row(new TextBlock { Text = L("New day starts at") }, reset));
        p.Add(Check(L("Show insights sidebar"), st.ShowInsights, v => st.ShowInsights = v));
        p.Add(Note(L("Usage follows the foreground app, pauses after 5 min without input, and is stored only on this PC.")));
        p.Add(Action(L("Reset today's data"), st.ResetToday));

        p.Add(Section(L("Category colours")));
        foreach (var category in Enum.GetValues<AppCategory>())
        {
            var row = Row(new TextBlock { Text = ScreenTimeStore.Title(category), Width = 130 });
            foreach (var hex in ScreenTimeStore.Swatches)
            {
                var swatch = new Button { Classes = { "plain" }, Content = new Ellipse { Width = 16, Height = 16, Fill = new SolidColorBrush(Color.Parse(hex)) } };
                swatch.Click += (_, _) => st.SetColor(category, hex);
                row.Children.Add(swatch);
            }
            p.Add(row);
        }

        p.Add(Section(L("Apps – category & exclusion")));
        var apps = st.KnownApps;
        if (apps.Count == 0) p.Add(Note(L("No apps recorded yet.")));
        var categories = Enum.GetValues<AppCategory>();
        foreach (var app in apps)
        {
            var category = new ComboBox { ItemsSource = categories.Select(ScreenTimeStore.Title).ToList(), SelectedIndex = Array.IndexOf(categories, app.Category), Width = 160 };
            category.SelectionChanged += (_, _) => { if (category.SelectedIndex >= 0) st.SetCategory(app.Id, categories[category.SelectedIndex]); };
            var exclude = new CheckBox { Content = L("Hide"), IsChecked = st.IsExcluded(app.Id) };
            exclude.IsCheckedChanged += (_, _) => { if ((exclude.IsChecked == true) != st.IsExcluded(app.Id)) st.ToggleExcluded(app.Id); };
            p.Add(Row(new TextBlock { Text = app.Name, Width = 230, TextTrimming = TextTrimming.CharacterEllipsis }, category, exclude));
        }
    }
}
