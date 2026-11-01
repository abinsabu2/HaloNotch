#pragma warning disable CS0649, CS0414, CS0067 // some fields/events are only used by the Windows build
using System.Diagnostics;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using static HaloNotch.Loc;
#if WINDOWS
using Windows.Media;
using Windows.Media.Control;
#endif

namespace HaloNotch;

/// Now playing + transport controls for any app that reports to Windows' media controls
/// (Spotify, browsers, Media Player, …) – the counterpart of the Mac app's Spotify/Music AppleScript.
public sealed class MediaController : Observable
{
    public sealed record NowPlaying(string App, string AppId, string Title, string Artist, string Album, bool IsPlaying,
                                    double Duration, double Position, DateTime FetchedAt, bool Shuffle, bool Repeat)
    {
        /// Playback position extrapolated to `at` (smooth progress between updates).
        public double Elapsed(DateTime at)
        {
            var v = IsPlaying ? Position + (at - FetchedAt).TotalSeconds : Position;
            return Duration > 0 ? Math.Clamp(v, 0, Duration) : Math.Max(v, 0);
        }
    }

    NowPlaying? now;
    Bitmap? artwork;
    string artworkKey = "";

    public NowPlaying? Now => now;
    public Bitmap? Artwork => artwork;
    public bool HasMedia => now != null;
    public bool NoMedia => now == null;
    public bool IsPlaying => now?.IsPlaying == true;
    public string Title => now?.Title ?? L("Nothing playing");
    public string Artist => now?.Artist ?? "";
    public string ArtistAlbum => now == null ? "" : string.IsNullOrEmpty(now.Album) ? now.Artist : $"{now.Artist} · {now.Album}";
    public string Source => now == null ? "" : $"{L("Now Playing")} · {now.App}";
    public string PlayGlyph => IsPlaying ? "" : "";       // Pause / Play
    public double Progress => now is { Duration: > 0 } n ? n.Elapsed(DateTime.Now) / n.Duration : 0;
    public string ElapsedText => now == null ? "" : Clock(now.Elapsed(DateTime.Now));
    public string RemainingText => now is { Duration: > 0 } n ? "-" + Clock(n.Duration - n.Elapsed(DateTime.Now)) : "";
    public bool Shuffle => now?.Shuffle == true;
    public bool Repeat => now?.Repeat == true;

    /// Raised when playback starts/stops (drives the real equalizer).
    public event Action? PlayingChanged;

    static string Clock(double s) => TimeSpan.FromSeconds(Math.Max(s, 0)) is var t && t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    /// Called every second by visible progress bars.
    public void Tick()
    {
        if (now?.IsPlaying == true) { Changed(nameof(Progress)); Changed(nameof(ElapsedText)); Changed(nameof(RemainingText)); }
    }

    void Publish(NowPlaying? value)
    {
        bool wasPlaying = IsPlaying;
        now = value;
        ChangedAll();
        if (wasPlaying != IsPlaying) PlayingChanged?.Invoke();
    }

#if WINDOWS
    GlobalSystemMediaTransportControlsSessionManager? manager;
    GlobalSystemMediaTransportControlsSession? session;

    public async void Start()
    {
        try
        {
            manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            manager.CurrentSessionChanged += (_, _) => Dispatcher.UIThread.Post(Attach);
            Attach();
        }
        catch (Exception e) { Debug.WriteLine($"HaloNotch: media controls unavailable – {e.Message}"); }
    }

    void Attach()
    {
        if (session != null)
        {
            session.MediaPropertiesChanged -= OnSessionChanged;
            session.PlaybackInfoChanged -= OnSessionChanged;
            session.TimelinePropertiesChanged -= OnSessionChanged;
        }
        session = manager?.GetCurrentSession();
        if (session != null)
        {
            session.MediaPropertiesChanged += OnSessionChanged;
            session.PlaybackInfoChanged += OnSessionChanged;
            session.TimelinePropertiesChanged += OnSessionChanged;
        }
        Refresh();
    }

    void OnSessionChanged(GlobalSystemMediaTransportControlsSession s, object e) => Dispatcher.UIThread.Post(Refresh);

    public async void Refresh()
    {
        var s = session;
        if (s == null) { Publish(null); return; }
        try
        {
            var props = await s.TryGetMediaPropertiesAsync();
            var info = s.GetPlaybackInfo();
            var timeline = s.GetTimelineProperties();
            bool playing = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var position = timeline.Position.TotalSeconds;
            if (playing && timeline.LastUpdatedTime > DateTimeOffset.MinValue)
                position += (DateTimeOffset.Now - timeline.LastUpdatedTime).TotalSeconds;
            var duration = (timeline.EndTime - timeline.StartTime).TotalSeconds;
            Publish(new NowPlaying(AppName(s.SourceAppUserModelId), s.SourceAppUserModelId, props.Title ?? "", props.Artist ?? "",
                props.AlbumTitle ?? "", playing, duration, position, DateTime.Now, info.IsShuffleActive == true,
                info.AutoRepeatMode is MediaPlaybackAutoRepeatMode.List or MediaPlaybackAutoRepeatMode.Track));

            var key = $"{props.Title}|{props.Artist}|{props.AlbumTitle}";
            if (key != artworkKey)
            {
                artworkKey = key;
                artwork = null;
                if (props.Thumbnail != null)
                {
                    using var stream = await props.Thumbnail.OpenReadAsync();
                    using var input = stream.AsStreamForRead();
                    artwork = Bitmap.DecodeToWidth(input, 300);
                }
                Changed(nameof(Artwork));
            }
        }
        catch (Exception e) { Debug.WriteLine($"HaloNotch: could not read player – {e.Message}"); }
    }

    /// "Spotify.exe" → "Spotify", "Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic" → "ZuneMusic".
    static string AppName(string aumid)
    {
        var name = aumid.Contains('!') ? aumid[(aumid.LastIndexOf('!') + 1)..] : aumid;
        name = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
        return name.Contains('.') ? name[(name.LastIndexOf('.') + 1)..] : name;
    }

    public async void PlayPause() { if (session != null) await session.TryTogglePlayPauseAsync(); }
    public async void Next() { if (session != null) await session.TrySkipNextAsync(); }
    public async void Previous() { if (session != null) await session.TrySkipPreviousAsync(); }

    public async void Seek(double fraction)
    {
        if (session == null || now is not { Duration: > 0 } n) return;
        await session.TryChangePlaybackPositionAsync(TimeSpan.FromSeconds(n.Duration * Math.Clamp(fraction, 0, 1)).Ticks);
    }

    public async void ToggleShuffle() { if (session != null && now != null) await session.TryChangeShuffleActiveAsync(!now.Shuffle); }

    public async void ToggleRepeat()
    {
        if (session != null && now != null)
            await session.TryChangeAutoRepeatModeAsync(now.Repeat ? MediaPlaybackAutoRepeatMode.None : MediaPlaybackAutoRepeatMode.List);
    }

    /// Brings the playing app to the front (works for Store apps and apps with a registered AppUserModelID).
    public void OpenPlayer()
    {
        if (now == null) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{now.AppId}") { UseShellExecute = true }); }
        catch (Exception e) { Debug.WriteLine($"HaloNotch: could not open player – {e.Message}"); }
    }
#else
    public void Start() { }
    public void PlayPause() { }
    public void Next() { }
    public void Previous() { }
    public void Seek(double fraction) { }
    public void ToggleShuffle() { }
    public void ToggleRepeat() { }
    public void OpenPlayer() { }
#endif
}
