using static HaloNotch.Loc;

namespace HaloNotch;

public enum NotchTab { Dashboard, Weather, ScreenTime, Clipboard, Files, Media, Calendar, Tools, Usage }

public static class Tabs
{
    public static string Title(NotchTab tab) => tab switch
    {
        NotchTab.Dashboard => L("Dashboard"), NotchTab.Weather => L("Weather"), NotchTab.ScreenTime => L("Screen Time"),
        NotchTab.Clipboard => L("Clipboard"), NotchTab.Files => L("Files"), NotchTab.Media => L("Media"),
        NotchTab.Calendar => L("Calendar"), NotchTab.Tools => L("Tools"), _ => L("Usage"),
    };

    /// Segoe Fluent Icons glyphs (Windows' counterpart of the Mac's SF Symbols).
    public static string Glyph(NotchTab tab) => tab switch
    {
        NotchTab.Dashboard => "", NotchTab.Weather => "", NotchTab.ScreenTime => "",
        NotchTab.Clipboard => "", NotchTab.Files => "", NotchTab.Media => "",
        NotchTab.Calendar => "", NotchTab.Tools => "", _ => "",
    };
}

/// The app's stores, shared by every notch window (one per display).
public static class Hub
{
    public static readonly ClipboardStore Clipboard = new();
    public static readonly MediaController Media = new();
    public static readonly SystemControls Controls = new();
    public static readonly WeatherStore Weather = new();
    public static readonly VaultStore Vault = new();
    public static readonly CalendarStore Calendar = new();
    public static readonly ScreenTimeStore ScreenTime = new();
    public static readonly FocusTimer Focus = new();
    public static readonly NotesStore Notes = new();
    public static readonly FileTrayStore Files = new();
    public static readonly ScreenshotStore Screenshots = new();
    public static readonly CameraController Camera = new();
    public static readonly UsageStore Usage = new();
    public static readonly AudioSpectrum Spectrum = new();
}
