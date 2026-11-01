using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using HaloNotch.Views;
using static HaloNotch.Loc;
#if WINDOWS
using HaloNotch.Win;
#endif

namespace HaloNotch;

/// Startup, tray menu, hotkey and one notch window per display (port of the Mac app's AppDelegate).
public partial class App : Application
{
    readonly List<NotchWindow> notches = [];
    IClassicDesktopStyleApplicationLifetime? desktop;
    IDisposable? rebuildTimer;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        desktop = ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        if (desktop != null)
        {
#if DEBUG
            ClipItem.SelfCheck();
            VaultStore.SelfCheck();
            CalendarStore.SelfCheck();
#endif
            Hub.Clipboard.Start();
            Hub.Media.Start();
            Hub.Controls.Start();
            Hub.Weather.Start();
            Hub.Calendar.Start();
            Hub.ScreenTime.Start();
            Hub.Screenshots.Start();
            // Real equalizer: analyse system audio only while music plays (and the option is on).
            Hub.Media.PlayingChanged += UpdateSpectrum;
            if (Hub.Usage.Enabled) DispatcherTimer.RunOnce(Hub.Usage.Refresh, TimeSpan.FromSeconds(30));   // tab opens with data
            Hub.Controls.Event += e =>
            {
                if (!Prefs.Get(e.IsVolume ? Prefs.VolumeHUD : Prefs.BrightnessHUD, true)) return;
                // Brightness refers to the built-in display only – show it on the primary notch.
                foreach (var n in notches) if (e.IsVolume || n.Screen.IsPrimary) n.ShowHud(e);
            };
#if WINDOWS
            MessageWindow.Shared.HotKey += () =>
            {
                // Open on the display the mouse is on.
                Native.GetCursorPos(out var p);
                var target = notches.FirstOrDefault(n => n.Screen.Bounds.Contains(new PixelPoint(p.X, p.Y))) ?? notches.FirstOrDefault();
                target?.OpenClipboardFromKeyboard();
            };
            MessageWindow.Shared.RegisterHotKey();
#endif
            SetupTray();
            RebuildNotches();
            desktop.ShutdownRequested += (_, _) => OnQuit();
#if DEBUG
            if (Environment.GetEnvironmentVariable("HALONOTCH_SNAPSHOT") is { Length: > 0 } dir) Snapshot.Run(notches[0], dir);
#endif
        }
        base.OnFrameworkInitializationCompleted();
    }

    void OnQuit()
    {
        Hub.Spectrum.Stop();
        Hub.Camera.Stop();
        Hub.ScreenTime.Save();
        Hub.Notes.Save();
    }

    void UpdateSpectrum()
    {
        if (Hub.Media.IsPlaying && Prefs.Get(Prefs.RealEqualizer, true)) Hub.Spectrum.Start(); else Hub.Spectrum.Stop();
    }

    // MARK: Displays

    /// One notch on every display ("all") or only on the main display.
    void RebuildNotches()
    {
        var probe = notches.FirstOrDefault() ?? new NotchWindow();
        var screens = probe.Screens.All.ToList();
        if (Prefs.Get(Prefs.DisplayMode, "all") == "main") screens = screens.Where(s => s.IsPrimary).Take(1).ToList();
        if (screens.Count == 0 && probe.Screens.Primary is { } primary) screens = [primary];

        for (int i = notches.Count - 1; i >= screens.Count; i--) { notches[i].Close(); notches.RemoveAt(i); }
        for (int i = 0; i < screens.Count; i++)
        {
            if (i < notches.Count) { notches[i].MoveTo(screens[i]); continue; }
            NotchWindow? window = null;
            window = new NotchWindow(screens[i], tab => MakeView(tab, window!));
            window.OpenSettingsRequested += _ => OpenSettings();
            window.Expanded += OnExpanded;
            window.Collapsed += () => Hub.Camera.Stop();     // camera only while the panel is open
            window.FilesDropped += OnFilesDropped;
            window.Screens.Changed += (_, _) => ScheduleRebuild();
            window.Show();
            notches.Add(window);
        }
    }

    /// Displays often report several changes in a row when plugged in – wait a moment.
    void ScheduleRebuild()
    {
        rebuildTimer?.Dispose();
        rebuildTimer = DispatcherTimer.RunOnce(RebuildNotches, TimeSpan.FromMilliseconds(600));
    }

    public void ApplyDisplayMode() => ScheduleRebuild();

    Control MakeView(NotchTab tab, NotchWindow window)
    {
        switch (tab)
        {
            case NotchTab.Dashboard: { var v = new DashboardView(); v.OpenSettings += OpenSettings; return v; }
            case NotchTab.Weather: { var v = new WeatherView(); v.OpenSettings += OpenSettings; return v; }
            case NotchTab.Calendar: { var v = new CalendarView(); v.OpenSettings += OpenSettings; return v; }
            case NotchTab.ScreenTime: return new ScreenTimeView();
            case NotchTab.Clipboard: return new ClipboardView(window, () => new VaultView());
            case NotchTab.Files: return new FilesView();
            case NotchTab.Media: return new MediaView(new SpectrumBars());
            case NotchTab.Tools: return new ToolsView();
            default: return new UsageView();
        }
    }

    /// Data work after the opening animation, so it can't cause a hitch.
    void OnExpanded() => DispatcherTimer.RunOnce(() =>
    {
        Hub.Weather.RefreshIfStale();
        Hub.Calendar.RefreshIfStale();
        Hub.Files.PruneMissing();
#if WINDOWS
        Hub.Media.Refresh();
#endif
    }, TimeSpan.FromMilliseconds(480));

    void OnFilesDropped(string[] paths) => Hub.Files.Add(paths);

    SettingsWindow? settings;

    public void OpenSettings()
    {
        if (settings == null)
        {
            settings = new SettingsWindow(ApplyDisplayMode, () => { foreach (var n in notches) n.RefreshDashboard(); });
            settings.Closed += (_, _) => settings = null;
        }
        settings.Show();
        settings.Activate();
    }

    // MARK: Tray menu (Mac: menu bar status item)

    void SetupTray()
    {
        var menu = new NativeMenu();
        menu.Add(Item(L("Clear Clipboard History"), Hub.Clipboard.Clear));
        menu.Add(Item(L("Clear File Tray"), Hub.Files.Clear));
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(Toggle(L("Ignore Password Manager Copies (Enpass …)"), Prefs.IgnorePasswordManagers));
        menu.Add(Toggle(L("Paste directly with Ctrl+Alt+V"), Prefs.PasteDirectly));
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(Toggle(L("Real Equalizer (System Audio)"), Prefs.RealEqualizer, UpdateSpectrum));
        menu.Add(Toggle(L("Show Volume HUD"), Prefs.VolumeHUD));
        menu.Add(Toggle(L("Show Brightness HUD"), Prefs.BrightnessHUD));
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(Item(L("Settings…"), OpenSettings));
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(Item(L("Quit HaloNotch"), () => desktop?.Shutdown()));

        var tray = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://HaloNotch/Assets/TrayIcon.ico"))),
            ToolTipText = "HaloNotch",
            Menu = menu,
        };
        tray.Clicked += (_, _) => notches.FirstOrDefault()?.OpenFor(TimeSpan.FromSeconds(4));
        TrayIcon.SetIcons(this, [tray]);
    }

    static NativeMenuItem Item(string title, Action action)
    {
        var item = new NativeMenuItem(title);
        item.Click += (_, _) => action();
        return item;
    }

    static NativeMenuItem Toggle(string title, string key, Action? changed = null)
    {
        var item = new NativeMenuItem(title) { ToggleType = MenuItemToggleType.CheckBox, IsChecked = Prefs.Get(key, true) };
        item.Click += (_, _) =>
        {
            var value = !Prefs.Get(key, true);
            Prefs.Set(key, value);
            item.IsChecked = value;
            changed?.Invoke();
        };
        return item;
    }
}
