using System.Diagnostics;
using Avalonia.Threading;
using static HaloNotch.Loc;

namespace HaloNotch;

/// Focus timer (Pomodoro): after focus the break starts automatically; after a break it waits for the user.
public sealed class FocusTimer : Observable
{
    public enum Phase { Focus, Rest }
    public sealed record Preset(int Focus, int Rest) { public string Label => $"{Focus}/{Rest}"; }
    public static readonly Preset[] Presets = [new(25, 5), new(50, 10), new(15, 3)];

    Phase phase = Phase.Focus;
    bool running;
    DateTime? endDate;
    double pausedRemaining;
    IDisposable? finishTimer;
    readonly DispatcherTimer ticker;

    public FocusTimer()
    {
        CurrentPreset = new Preset(Prefs.Get("focus.minutes", 25), Prefs.Get("focus.break", 5));
        pausedRemaining = Duration;
        ticker = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => ChangedAll());
    }

    public Preset CurrentPreset { get; private set; }
    public double Duration => (phase == Phase.Focus ? CurrentPreset.Focus : CurrentPreset.Rest) * 60;
    public bool IsRunning => running;
    public bool IsRest => phase == Phase.Rest;
    /// True while a session is running or paused part-way.
    public bool IsActive => running || pausedRemaining < Duration - 0.5;
    public double Remaining => running && endDate is { } end ? Math.Max((end - DateTime.Now).TotalSeconds, 0) : pausedRemaining;
    public double Progress => Duration > 0 ? 1 - Remaining / Duration : 0;
    public string RemainingText => TimeSpan.FromSeconds(Math.Ceiling(Remaining)).ToString(@"mm\:ss");
    public string PhaseText => phase == Phase.Focus ? L("Focus") : L("Break");
    public string PhaseCaps => PhaseText.ToUpperInvariant();
    public string StartLabel => running ? L("Pause") : IsActive ? L("Resume") : L("Start");
    public string StartGlyph => running ? "" : "";
    public string PresetLabel => CurrentPreset.Label;

    public int SessionsToday =>
        Prefs.Get("focus.day", "") == DateTime.Today.ToString("yyyy-MM-dd") ? Prefs.Get("focus.sessions", 0) : 0;
    public string SessionsText => LF("Sessions today: %d", SessionsToday);

    public void Toggle() { if (running) Pause(); else Start(); }

    public void Start()
    {
        if (running) return;
        endDate = DateTime.Now.AddSeconds(pausedRemaining);
        running = true;
        finishTimer?.Dispose();
        finishTimer = DispatcherTimer.RunOnce(Finished, TimeSpan.FromSeconds(pausedRemaining));
        ticker.Start();
        ChangedAll();
    }

    public void Pause()
    {
        if (!running) return;
        pausedRemaining = Remaining;
        running = false;
        endDate = null;
        finishTimer?.Dispose();
        ticker.Stop();
        ChangedAll();
    }

    public void Reset()
    {
        Pause();
        phase = Phase.Focus;
        pausedRemaining = Duration;
        ChangedAll();
    }

    public void Skip() => Advance(countSession: false, autoStart: running);

    public void Choose(Preset preset)
    {
        CurrentPreset = preset;
        Prefs.Set("focus.minutes", preset.Focus);
        Prefs.Set("focus.break", preset.Rest);
        Reset();
    }

    void Finished()
    {
#if WINDOWS
        Win.Native.MessageBeep(0x40);              // system "asterisk" sound (Mac: Glass / Hero)
#endif
        Advance(countSession: phase == Phase.Focus, autoStart: phase == Phase.Focus);
    }

    void Advance(bool countSession, bool autoStart)
    {
        finishTimer?.Dispose();
        if (countSession)
        {
            var sessions = SessionsToday + 1;
            Prefs.Set("focus.day", DateTime.Today.ToString("yyyy-MM-dd"));
            Prefs.Set("focus.sessions", sessions);
        }
        phase = phase == Phase.Focus ? Phase.Rest : Phase.Focus;
        running = false;
        endDate = null;
        ticker.Stop();
        pausedRemaining = Duration;
        if (autoStart) Start();
        ChangedAll();
    }
}

/// Quick notes, saved 0.7 s after the last keystroke to %APPDATA%\HaloNotch\notes.txt.
public sealed class NotesStore : Observable
{
    static string FilePath => Path.Combine(Prefs.Dir, "notes.txt");
    string text;
    IDisposable? saveTimer;

    public NotesStore()
    {
        try { text = File.ReadAllText(FilePath); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { text = ""; }
    }

    public string Text
    {
        get => text;
        set
        {
            if (!Set(ref text, value)) return;
            saveTimer?.Dispose();
            saveTimer = DispatcherTimer.RunOnce(Save, TimeSpan.FromMilliseconds(700));
        }
    }

    public void Save()
    {
        try { Directory.CreateDirectory(Prefs.Dir); Prefs.WriteAtomic(FilePath, text); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Debug.WriteLine($"HaloNotch: notes not saved – {e.Message}"); }
    }
}
