using Avalonia.Controls;
using Avalonia.Interactivity;
using static HaloNotch.Loc;

namespace HaloNotch.Views;

/// Tools tab: focus timer | quick notes | camera mirror.
public partial class ToolsView : UserControl
{
    public ToolsView()
    {
        InitializeComponent();
        DataContext = Hub.Focus;
        NotesTitle.Text = L("Notes").ToUpperInvariant();
        SavedLabel.Text = L("Saved automatically");
        NotesBox.PlaceholderText = L("Jot something down…");
        CameraTitle.Text = L("Camera").ToUpperInvariant();
        CameraOn.Content = L("Turn on camera");
        CameraOff.Content = L("Turn off");
        CameraHint.Text = L("Quick check before a meeting");
        DeniedText.Text = L("Camera access is off");
        OpenPrivacy.Content = L("Open Settings");
        ToolTip.SetTip(ResetButton, L("Reset"));
        ToolTip.SetTip(SkipButton, L("Skip"));
        ToolTip.SetTip(PresetButton, L("Focus / break length"));
        Hub.Camera.FrameArrived += () => CameraImage.InvalidateVisual();
        DetachedFromVisualTree += (_, _) => Hub.Camera.Stop();       // camera only while the tab is visible
    }

    void OnToggle(object? sender, RoutedEventArgs e) => Hub.Focus.Toggle();
    void OnReset(object? sender, RoutedEventArgs e) => Hub.Focus.Reset();
    void OnSkip(object? sender, RoutedEventArgs e) => Hub.Focus.Skip();

    /// Cycles through the presets 25/5 → 50/10 → 15/3.
    void OnPreset(object? sender, RoutedEventArgs e)
    {
        int i = Array.IndexOf(FocusTimer.Presets, Hub.Focus.CurrentPreset);
        Hub.Focus.Choose(FocusTimer.Presets[(i + 1) % FocusTimer.Presets.Length]);
    }

    void OnCameraOn(object? sender, RoutedEventArgs e) => Hub.Camera.Start();
    void OnCameraOff(object? sender, RoutedEventArgs e) => Hub.Camera.Stop();
    void OnPrivacy(object? sender, RoutedEventArgs e) => Hub.Camera.OpenPrivacySettings();
}
