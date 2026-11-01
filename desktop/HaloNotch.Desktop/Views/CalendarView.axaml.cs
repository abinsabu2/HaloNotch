using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using static HaloNotch.Loc;

namespace HaloNotch.Views;

/// Calendar tab: month grid | selected day's events (with Join for Teams / Zoom / Meet links).
public partial class CalendarView : UserControl
{
    public static string JoinText => L("Join");
    public event Action? OpenSettings;
    CalendarStore Store => Hub.Calendar;
    static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#F2BD85"));

    public CalendarView()
    {
        InitializeComponent();
        DataContext = Hub.Calendar;
        TodayButton.Content = L("Today");
        NoEventsText.Text = L("No events");
        NoFeedsText.Text = L("Add your Outlook, Google or iCloud calendar link (ICS) in Settings to see your events here.");
        AddButton.Content = L("Add calendar");
        ToolTip.SetTip(PrevYear, L("Previous year")); ToolTip.SetTip(NextYear, L("Next year"));
        ToolTip.SetTip(PrevMonth, L("Previous month")); ToolTip.SetTip(NextMonth, L("Next month"));
        foreach (var s in CalendarStore.WeekdaySymbols)
            Weekdays.Children.Add(new TextBlock { Text = s, Classes = { "tertiary", "small" }, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center });
        Store.PropertyChanged += (_, _) => BuildDays();
        BuildDays();
    }

    void BuildDays()
    {
        Days.Children.Clear();
        foreach (var cell in Store.GridCells)
        {
            if (cell is not { } day) { Days.Children.Add(new Panel()); continue; }
            bool selected = day == Store.Selected, today = day == DateTime.Today, hasEvents = Store.EventDays.Contains(day);
            var label = new TextBlock
            {
                Text = day.Day.ToString(), FontSize = 12, FontWeight = selected || today ? FontWeight.Bold : FontWeight.Medium,
                Foreground = selected ? Brushes.Black : today ? Accent : Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            var dot = new Ellipse { Width = 4, Height = 4, Fill = selected ? Brushes.Black : Accent, IsVisible = hasEvents,
                                    VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new(0, 0, 0, 2) };
            var button = new Button
            {
                Classes = { "plain" }, Width = 28, Height = 28, CornerRadius = new(14),
                Background = selected ? new SolidColorBrush(Color.Parse("#F2BD85")) : Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Center, Content = new Panel { Children = { label, dot } },
            };
            button.Click += (_, _) => Store.Select(day);
            Days.Children.Add(button);
        }
    }

    void OnPrevYear(object? s, RoutedEventArgs e) => Store.ShiftYear(-1);
    void OnNextYear(object? s, RoutedEventArgs e) => Store.ShiftYear(1);
    void OnPrevMonth(object? s, RoutedEventArgs e) => Store.ShiftMonth(-1);
    void OnNextMonth(object? s, RoutedEventArgs e) => Store.ShiftMonth(1);
    void OnToday(object? s, RoutedEventArgs e) => Store.GoToToday();
    void OnSettings(object? s, RoutedEventArgs e) => OpenSettings?.Invoke();
    void OnJoin(object? s, RoutedEventArgs e) { if ((s as Control)?.DataContext is CalendarEntry entry) Store.Join(entry); }
}
