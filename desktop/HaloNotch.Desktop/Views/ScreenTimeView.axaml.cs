using Avalonia.Controls;
using static HaloNotch.Loc;

namespace HaloNotch.Views;

/// Screen Time tab: categories donut | top apps | insights + activity by hour.
public partial class ScreenTimeView : UserControl
{
    public ScreenTimeView()
    {
        InitializeComponent();
        DataContext = Hub.ScreenTime;
        CategoriesTitle.Text = L("Categories").ToUpperInvariant();
        TopAppsTitle.Text = L("Top apps").ToUpperInvariant();
        InsightsTitle.Text = L("Insights").ToUpperInvariant();
        TodayLabel.Text = L("today");
        SwitchesLabel.Text = L("App switches");
        YesterdayLabel.Text = L("vs yesterday");
        HourTitle.Text = L("ACTIVITY BY HOUR");
        EmptyText.Text = L("Tracking started – use your PC and check back.");
    }
}
