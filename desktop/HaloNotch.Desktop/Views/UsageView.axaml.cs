using Avalonia.Controls;
using Avalonia.Interactivity;
using static HaloNotch.Loc;

namespace HaloNotch.Views;

/// Usage tab: Claude Code | Codex | activity heatmap. Opt-in, first scan on first open.
public partial class UsageView : UserControl
{
    public static string FreshLabel => L("fresh tokens today");
    public static string InputLabel => L("Input");
    public static string CacheReadLabel => L("Cache read");
    public static string CacheWriteLabel => L("Cache write");
    public static string ProcessedLabel => L("Processed");
    public static string OutputLabel => L("Output");
    public static string SessionsLabel => L("Sessions");

    public UsageView()
    {
        InitializeComponent();
        DataContext = Hub.Usage;
        OptInText.Text = L("Show token usage by reading the local Claude Code and Codex session logs? Only numbers are read; nothing leaves this PC.");
        EnableButton.Content = L("Enable");
        LoadingText.Text = L("Reading session logs…");
        NotFoundText.Text = L("No session logs found on this PC.");
        ActivityTitle.Text = L("Activity").ToUpperInvariant();
        AllChip.Content = L("All");
        Footnote.Text = L("Fresh tokens = input + cache write + output. Cache reads are not counted.");
        AttachedToVisualTree += (_, _) => { if (Hub.Usage.Enabled) Hub.Usage.Activate(); };
        Hub.Usage.PropertyChanged += (_, _) => UpdateChips();
        UpdateChips();
    }

    void UpdateChips()
    {
        AllChip.Classes.Set("on", Hub.Usage.Source == "all");
        ClaudeChip.Classes.Set("on", Hub.Usage.Source == "claude");
        CodexChip.Classes.Set("on", Hub.Usage.Source == "codex");
    }

    void OnEnable(object? sender, RoutedEventArgs e) => Hub.Usage.Enabled = true;
    void OnSource(object? sender, RoutedEventArgs e) { if ((sender as Button)?.Tag is string s) Hub.Usage.Source = s; }
}
