#pragma warning disable CS0649, CS0067 // used by the Windows build only
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
#if WINDOWS
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
#endif

namespace HaloNotch;

/// Camera mirror (Tools tab): live, mirrored preview – only while the user has it on.
public sealed class CameraController : Observable
{
    public enum State { Off, Starting, Running, Denied }

    State state = State.Off;
    public State Status => state;
    public bool IsOff => state is State.Off or State.Starting;
    public bool IsRunning => state == State.Running;
    public bool IsDenied => state == State.Denied;

    /// Latest frame (BGRA). The view redraws on FrameArrived.
    public WriteableBitmap? Frame { get; private set; }
    public event Action? FrameArrived;

    void SetState(State value) { state = value; ChangedAll(); }

    public void OpenPrivacySettings() => Shell.Open("ms-settings:privacy-webcam");

#if WINDOWS
    MediaCapture? capture;
    MediaFrameReader? reader;
    DateTime lastFrame;
    bool frameQueued;

    public async void Start()
    {
        if (state is State.Running or State.Starting) return;
        SetState(State.Starting);
        try
        {
            var groups = await MediaFrameSourceGroup.FindAllAsync();
            var group = groups.FirstOrDefault(g => g.SourceInfos.Any(s => s.SourceKind == MediaFrameSourceKind.Color));
            if (group == null) { SetState(State.Off); return; }
            capture = new MediaCapture();
            await capture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                SourceGroup = group,
                SharingMode = MediaCaptureSharingMode.SharedReadOnly,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                StreamingCaptureMode = StreamingCaptureMode.Video,
            });
            var source = capture.FrameSources.Values.First(s => s.Info.SourceKind == MediaFrameSourceKind.Color);
            reader = await capture.CreateFrameReaderAsync(source);
            reader.FrameArrived += OnFrame;
            await reader.StartAsync();
            SetState(State.Running);
        }
        catch (UnauthorizedAccessException) { Cleanup(); SetState(State.Denied); }      // Settings → Privacy → Camera
        catch (Exception e) { Debug.WriteLine($"HaloNotch: camera failed – {e.Message}"); Cleanup(); SetState(State.Off); }
    }

    public void Stop()
    {
        if (state != State.Running && state != State.Starting) return;
        Cleanup();
        SetState(State.Off);
    }

    void Cleanup()
    {
        if (reader != null) { reader.FrameArrived -= OnFrame; _ = reader.StopAsync(); reader.Dispose(); reader = null; }
        capture?.Dispose();
        capture = null;
    }

    /// Worker thread: convert to BGRA (premultiplied), copy out, hand over to the UI thread (~20 fps max).
    void OnFrame(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        if (frameQueued || (DateTime.Now - lastFrame).TotalMilliseconds < 50) return;
        using var frame = sender.TryAcquireLatestFrame();
        var source = frame?.VideoMediaFrame?.SoftwareBitmap;
        if (source == null) return;
        using var bgra = SoftwareBitmap.Convert(source, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        int w = bgra.PixelWidth, h = bgra.PixelHeight;
        var buffer = new Windows.Storage.Streams.Buffer((uint)(w * h * 4));
        bgra.CopyToBuffer(buffer);
        var pixels = buffer.ToArray();
        lastFrame = DateTime.Now;
        frameQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            frameQueued = false;
            if (state != State.Running) return;
            if (Frame == null || Frame.PixelSize.Width != w || Frame.PixelSize.Height != h)
            {
                Frame = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
                Changed(nameof(Frame));
            }
            using (var locked = Frame.Lock())
                for (int y = 0; y < h; y++) Marshal.Copy(pixels, y * w * 4, locked.Address + y * locked.RowBytes, w * 4);
            FrameArrived?.Invoke();
        });
    }
#else
    public void Start() => SetState(State.Denied);
    public void Stop() => SetState(State.Off);
#endif
}
