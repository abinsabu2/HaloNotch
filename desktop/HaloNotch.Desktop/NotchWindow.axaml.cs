using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using static HaloNotch.Loc;

namespace HaloNotch;

public partial class NotchWindow : Window
{
    static readonly string[] Tabs =
        ["Dashboard", "Weather", "Screen Time", "Clipboard", "Files", "Media", "Calendar", "Tools", "Usage"];

    CancellationTokenSource? closing;

    public NotchWindow()
    {
        InitializeComponent();
        foreach (var tab in Tabs)
            Dock.Children.Add(new TextBlock { Text = L(tab), Foreground = Brushes.Gray });

        Shape.PointerEntered += (_, _) => { closing?.Cancel(); SetExpanded(true); };
        Shape.PointerExited += async (_, _) =>
        {
            closing = new CancellationTokenSource();
            try { await Task.Delay(220, closing.Token); SetExpanded(false); }   // same grace delay as the Mac app
            catch (TaskCanceledException) { }
        };
        SizeChanged += (_, _) => CenterOnTop();   // SizeToContent resizes the window; keep it centred
        Opened += (_, _) => { ExcludeFromCapture(); CenterOnTop(); };
    }

    // ponytail: snaps between sizes; the open/close morph animation comes in Phase 1.
    void SetExpanded(bool expanded)
    {
        Shape.Width = expanded ? 860 : 200;
        Shape.Height = expanded ? 300 : 8;
        Shape.CornerRadius = expanded ? new CornerRadius(0, 0, 28, 28) : new CornerRadius(0, 0, 8, 8);
        Body.IsVisible = expanded;
    }

    void CenterOnTop()
    {
        if (Screens.Primary is not { } screen) return;
        var width = (int)(ClientSize.Width * screen.Scaling);   // DIPs → physical pixels
        Position = new PixelPoint(screen.Bounds.X + (screen.Bounds.Width - width) / 2, screen.Bounds.Y);
    }

    /// Keeps clipboard, calendar and notes out of screen shares and recordings (like the Mac panel).
    void ExcludeFromCapture()
    {
        if (OperatingSystem.IsWindows() && TryGetPlatformHandle() is { } handle)
            SetWindowDisplayAffinity(handle.Handle, 0x11);   // WDA_EXCLUDEFROMCAPTURE (Windows 10 2004+)
    }

    [DllImport("user32.dll")]
    static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
}
