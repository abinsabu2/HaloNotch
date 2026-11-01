using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using static HaloNotch.Loc;
#if WINDOWS
using HaloNotch.Win;
#endif

namespace HaloNotch;

/// Counts open context menus / flyouts: the panel must not close while one is open (it lives in its own popup).
public static class Popups
{
    public static int Open;

    public static void Track(ContextMenu menu)
    {
        menu.Opened += (_, _) => Open++;
        menu.Closed += (_, _) => Open = Math.Max(0, Open - 1);
    }
}

/// One notch per display: a thin handle at the top centre that opens into the panel on hover.
/// Port of the Mac app's NotchController (hover tracking, open/close morph, HUD, ⌃⌥V keyboard mode).
public partial class NotchWindow : Window
{
    const double PanelW = 920, PanelH = 300, DockGap = 10, DockH = 46;
    const double HandleW = 200, HandleH = 8, HudW = 320, HudH = 36;

    public Screen Screen { get; private set; }
    public NotchTab Selected { get; private set; } = NotchTab.Dashboard;
    public bool IsExpanded { get; private set; }
    public bool KeyboardMode { get; private set; }
    public bool IsDropTargeted { get; private set; }

    public event Action<NotchWindow>? OpenSettingsRequested;
    public event Action? KeyboardModeChanged;

    readonly Dictionary<NotchTab, Control> views = [];
    readonly Dictionary<NotchTab, Button> dockButtons = [];
    readonly Func<NotchTab, Control> makeView;
    readonly DispatcherTimer hoverTimer, clockTimer;
    IDisposable? hudTimer;
    DateTime outsideSince = DateTime.MaxValue;
    DateTime holdUntil = DateTime.MinValue;
    int morphGeneration;
#if WINDOWS
    IntPtr previousForeground;
    bool activated;
#endif

    public NotchWindow() : this(null!, _ => new Panel()) { }     // designer

    public NotchWindow(Screen screen, Func<NotchTab, Control> makeView)
    {
        InitializeComponent();
        Screen = screen;
        this.makeView = makeView;

        foreach (var tab in Enum.GetValues<NotchTab>())
        {
            var button = new Button { Classes = { "dock" }, Content = Tabs.Glyph(tab) };
            ToolTip.SetTip(button, Tabs.Title(tab));
            button.Click += (_, _) => Select(tab);
            dockButtons[tab] = button;
            DockItems.Children.Add(button);
        }
        SettingsButton.Click += (_, _) => OpenSettingsRequested?.Invoke(this);
        ReminderHeader.Text = L("Reminders").ToUpperInvariant();
        ReminderDone.Content = L("Done");
        ReminderDone.Click += (_, _) => Hub.Reminders.Complete();
        ToolTip.SetTip(SettingsButton, L("Settings"));

        PointerEntered += (_, _) => { if (!IsExpanded && Hud.IsVisible == false) SetExpanded(true); };
        PointerMoved += (_, _) => { if (!IsExpanded) SetExpanded(true); };
        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => IsDropTargeted = false);
        AddHandler(DragDrop.DropEvent, OnDrop);
        KeyDown += OnKeyDown;

        // Typing into a text field needs keyboard focus: the window is non-activating until one is clicked.
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.Source is Visual v && (v is TextBox || v.FindAncestorOfType<TextBox>() != null)) EnableActivation();
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);

        hoverTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(120), DispatcherPriority.Input, (_, _) => CheckPointer());
        clockTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => UpdateClock());

        Opened += (_, _) =>
        {
#if WINDOWS
            if (TryGetPlatformHandle() is { } handle)
            {
                // Keep clipboard, calendar and notes out of screen shares and recordings (like the Mac panel).
                Native.SetWindowDisplayAffinity(handle.Handle, Native.WDA_EXCLUDEFROMCAPTURE);
                Native.SetNoActivate(handle.Handle, true);
            }
#endif
            Place(HandleW, HandleH);
        };
        UpdateDock();
    }

    public void MoveTo(Screen screen)
    {
        Screen = screen;
        if (IsExpanded) Place(PanelW, PanelH + DockGap + DockH); else Place(HandleW, HandleH);
    }

    // MARK: Geometry

    /// Sizes the window and centres it at the top of its display.
    void Place(double width, double height)
    {
        Width = width;
        Height = height;
        var b = Screen.Bounds;
        Position = new PixelPoint(b.X + (int)((b.Width - width * Screen.Scaling) / 2), b.Y);
    }

    bool PointerInside()
    {
#if WINDOWS
        if (Native.GetCursorPos(out var p))
        {
            var s = Screen.Scaling;
            int pad = (int)(8 * s);
            return p.X >= Position.X - pad && p.X <= Position.X + Width * s + pad
                && p.Y >= Position.Y - pad && p.Y <= Position.Y + Height * s + pad;
        }
#endif
        return IsPointerOver;
    }

    bool Busy =>
        KeyboardMode || IsDropTargeted || Popups.Open > 0 || ReminderCard.IsVisible
        || (IsActive && FocusManager?.GetFocusedElement() is TextBox);

    /// Closes the panel once the pointer has been outside for 220 ms (same grace delay as the Mac app).
    void CheckPointer()
    {
        if (!IsExpanded) { hoverTimer.Stop(); return; }
        bool inside = PointerInside();
        if (inside) holdUntil = DateTime.MinValue;
        if (inside || Busy || DateTime.Now < holdUntil) { outsideSince = DateTime.MaxValue; return; }
        if (outsideSince == DateTime.MaxValue) outsideSince = DateTime.Now;
        else if ((DateTime.Now - outsideSince).TotalMilliseconds >= 220) SetExpanded(false);
    }

    // MARK: Open / close

    public void SetExpanded(bool expanded)
    {
        if (expanded == IsExpanded) return;
        IsExpanded = expanded;
        int generation = ++morphGeneration;
        if (expanded)
        {
            hudTimer?.Dispose();
            Hud.IsVisible = false;
            Place(PanelW, PanelH + DockGap + DockH);
            ShowTab(Selected);
            Body.IsVisible = true;
            Dock.IsVisible = true;
            Shape.Width = PanelW; Shape.Height = PanelH;
            Shape.CornerRadius = new CornerRadius(0, 0, 38, 38);
            Body.Opacity = 1; Dock.Opacity = 1;
            outsideSince = DateTime.MaxValue;
            hoverTimer.Start();
            UpdateClock();
            clockTimer.Start();
            Expanded?.Invoke();
        }
        else
        {
            hoverTimer.Stop();
            clockTimer.Stop();
            Body.Opacity = 0; Dock.Opacity = 0;
            Shape.Width = HandleW; Shape.Height = HandleH;
            Shape.CornerRadius = new CornerRadius(0, 0, 8, 8);
            RestoreFocus();
            Collapsed?.Invoke();
            // Shrink the window after the close animation (unless it re-opened meanwhile).
            DispatcherTimer.RunOnce(() =>
            {
                if (generation != morphGeneration) return;
                Body.IsVisible = false;
                Dock.IsVisible = false;
                Place(HandleW, HandleH);
            }, TimeSpan.FromMilliseconds(340));
        }
    }

    /// Opens without the pointer on it (tray icon): stays open for `hold` or until the pointer came and left.
    public void OpenFor(TimeSpan hold)
    {
        holdUntil = DateTime.Now + hold;
        SetExpanded(true);
    }

    /// Shows (or hides, null) the due reminder; `open` also opens the panel – it stays open until Done.
    public void ShowReminder(Reminder? reminder, bool open)
    {
        ReminderCard.IsVisible = reminder != null;
        if (reminder == null) return;
        ReminderTitle.Text = reminder.Title;
        ReminderTime.Text = reminder.Due.Date == DateTime.Today ? Fmt.Time(reminder.Due) : $"{Fmt.ShortDate(reminder.Due)} · {Fmt.Time(reminder.Due)}";
        if (open) SetExpanded(true);
    }

    /// Raised after opening – the app refreshes calendar, media and weather then (Mac: setExpanded).
    public event Action? Expanded;
    public event Action? Collapsed;

    /// Settings → Dashboard changed which sections are shown.
    public void RefreshDashboard()
    {
        if (views.GetValueOrDefault(NotchTab.Dashboard) is Views.DashboardView dashboard) dashboard.Arrange();
    }

    public void Select(NotchTab tab)
    {
        Selected = tab;
        if (IsExpanded) ShowTab(tab);
        UpdateDock();
    }

    void ShowTab(NotchTab tab)
    {
        if (!views.TryGetValue(tab, out var view)) views[tab] = view = makeView(tab);    // created on first use
        TabHost.Content = view;
        TitleText.Text = Tabs.Title(tab);
    }

    void UpdateDock()
    {
        foreach (var (tab, button) in dockButtons) button.Classes.Set("selected", tab == Selected);
    }

    void UpdateClock()
    {
        var now = DateTime.Now;
        DateText.Text = Fmt.ShortDate(now);
        TimeText.Text = Fmt.Time(now);
    }

    // MARK: HUD

    public void ShowHud(HudEvent e)
    {
        if (IsExpanded) return;
        int generation = ++morphGeneration;
        HudIcon.Text = e.IsVolume ? (e.Muted || e.Level == 0 ? "" : "") : "";
        HudLevel.Value = e.Muted ? 0 : e.Level;
        HudValue.Text = e.Muted ? "–" : $"{Math.Round(e.Level * 100)}";
        Body.IsVisible = false;
        Hud.IsVisible = true;
        Place(HudW, HudH);
        Shape.Width = HudW; Shape.Height = HudH;
        Shape.CornerRadius = new CornerRadius(0, 0, 18, 18);
        hudTimer?.Dispose();
        hudTimer = DispatcherTimer.RunOnce(() =>
        {
            if (generation != morphGeneration || IsExpanded) return;
            Hud.IsVisible = false;
            Shape.Width = HandleW; Shape.Height = HandleH;
            Shape.CornerRadius = new CornerRadius(0, 0, 8, 8);
            DispatcherTimer.RunOnce(() => { if (generation == morphGeneration && !IsExpanded) Place(HandleW, HandleH); },
                                    TimeSpan.FromMilliseconds(340));
        }, TimeSpan.FromSeconds(1.6));
    }

    // MARK: Files dropped onto the notch

    void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (!e.DataTransfer.Contains(DataFormat.File)) return;
        IsDropTargeted = true;
        Select(NotchTab.Files);
        SetExpanded(true);
        e.DragEffects = DragDropEffects.Link;
    }

    void OnDrop(object? sender, DragEventArgs e)
    {
        IsDropTargeted = false;
        var paths = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToArray() ?? [];
        if (paths.Length > 0) FilesDropped?.Invoke(paths);
    }

    public event Action<string[]>? FilesDropped;

    // MARK: Keyboard mode (Ctrl+Alt+V)

    public void OpenClipboardFromKeyboard()
    {
        if (KeyboardMode) { EndKeyboardMode(paste: false); return; }
        KeyboardMode = true;
        KeyboardModeChanged?.Invoke();
        Select(NotchTab.Clipboard);
        SetExpanded(true);
        EnableActivation();
        Focus();
    }

    void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!KeyboardMode) return;
        if (e.Key == Key.Escape) { EndKeyboardMode(paste: false); e.Handled = true; return; }
        int digit = e.Key is >= Key.D0 and <= Key.D9 ? e.Key - Key.D0 : e.Key is >= Key.NumPad0 and <= Key.NumPad9 ? e.Key - Key.NumPad0 : -1;
        if (digit < 0) return;
        e.Handled = true;
        int index = digit == 0 ? 9 : digit - 1;
        var items = Hub.Clipboard.Items;
        if (index >= items.Count) return;
        Hub.Clipboard.Copy(items[index]);
        EndKeyboardMode(paste: Prefs.Get(Prefs.PasteDirectly, true));
    }

    void EndKeyboardMode(bool paste)
    {
        KeyboardMode = false;
        KeyboardModeChanged?.Invoke();
        SetExpanded(false);                 // restores the previous app's focus
#if WINDOWS
        if (paste) DispatcherTimer.RunOnce(Native.SendPaste, TimeSpan.FromMilliseconds(120));
#endif
    }

    // MARK: Focus (Windows: non-activating until a text field or keyboard mode needs keys)

    void EnableActivation()
    {
#if WINDOWS
        if (activated || TryGetPlatformHandle() is not { } handle) return;
        previousForeground = Native.GetForegroundWindow();
        Native.SetNoActivate(handle.Handle, false);
        activated = true;
        Activate();
#endif
    }

    void RestoreFocus()
    {
#if WINDOWS
        if (!activated || TryGetPlatformHandle() is not { } handle) return;
        activated = false;
        Native.SetNoActivate(handle.Handle, true);
        if (previousForeground != IntPtr.Zero) Native.SetForegroundWindow(previousForeground);
#endif
    }
}
