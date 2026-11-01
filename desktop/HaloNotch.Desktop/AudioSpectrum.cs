using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
#if WINDOWS
using NAudio.Dsp;
using NAudio.Wave;
#endif

namespace HaloNotch;

/// Real equalizer: system audio via WASAPI loopback (nothing is recorded or stored) → FFT → 32 log-spaced bands
/// 40 Hz – 16 kHz, fast attack / slow release. Same maths as the Mac app's process tap. Runs only while music plays.
/// `Bands` is read by SpectrumBars on its own timer – intentionally not observable (keeps high-frequency updates out of bindings).
public sealed class AudioSpectrum
{
    public const int BandCount = 32;
    const int N = 1024;
    public readonly float[] Bands = new float[BandCount];
    public bool IsLive { get; private set; }

#if WINDOWS
#pragma warning disable CS0618 // WasapiLoopbackCapture: still the simplest system-wide loopback in NAudio 3
    WasapiLoopbackCapture? capture;
    readonly float[] buffer = new float[N];
    int filled;
    (int Lo, int Hi)[] bins = [];
    float[] gains = [];

    public void Start()
    {
        if (capture != null) return;
        try
        {
            capture = new WasapiLoopbackCapture();
            var format = capture.WaveFormat;
            double rate = format.SampleRate, minF = 40, maxF = Math.Min(16_000, rate / 2 - 1);
            bins = new (int, int)[BandCount];
            gains = new float[BandCount];
            for (int i = 0; i < BandCount; i++)
            {
                double f0 = minF * Math.Pow(maxF / minF, (double)i / BandCount), f1 = minF * Math.Pow(maxF / minF, (double)(i + 1) / BandCount);
                int lo = Math.Max(1, (int)(f0 * N / rate)), hi = Math.Max(lo + 1, (int)(f1 * N / rate));
                bins[i] = (lo, Math.Min(hi, N / 2));
                gains[i] = (float)i / (BandCount - 1) * 12;          // +0…12 dB tilt for highs
            }
            capture.DataAvailable += (_, e) => Process(e.Buffer, e.BytesRecorded, format);
            capture.RecordingStopped += (_, _) => IsLive = false;
            capture.StartRecording();
        }
        catch (Exception e) { Debug.WriteLine($"HaloNotch: equalizer unavailable – {e.Message}"); capture = null; }
    }

    public void Stop()
    {
        capture?.StopRecording();
        capture?.Dispose();
        capture = null;
        IsLive = false;
        Array.Clear(Bands);
    }

    /// Audio thread: mix to mono, analyse every 1024 samples.
    void Process(byte[] data, int bytes, WaveFormat format)
    {
        if (format.Encoding != WaveFormatEncoding.IeeeFloat && format.Encoding != WaveFormatEncoding.Extensible) return;
        int channels = format.Channels, frameBytes = 4 * channels;
        for (int offset = 0; offset + frameBytes <= bytes; offset += frameBytes)
        {
            float sum = 0;
            for (int c = 0; c < channels; c++) sum += BitConverter.ToSingle(data, offset + 4 * c);
            buffer[filled++] = sum / channels;
            if (filled == N) { Analyse(); filled = 0; }
        }
    }

    void Analyse()
    {
        var fft = new Complex[N];
        double rms = 0;
        for (int i = 0; i < N; i++)
        {
            rms += buffer[i] * buffer[i];
            fft[i].X = (float)(buffer[i] * FastFourierTransform.HannWindow(i, N));
        }
        rms = Math.Sqrt(rms / N);
        FastFourierTransform.FFT(true, 10, fft);              // 2^10 = 1024; NAudio scales by 1/N
        bool live = rms > 0.0003;
        for (int b = 0; b < BandCount; b++)
        {
            var (lo, hi) = bins[b];
            float mean = 0;
            for (int k = lo; k < hi; k++) mean += fft[k].X * fft[k].X + fft[k].Y * fft[k].Y;
            mean /= Math.Max(hi - lo, 1);
            float amplitude = MathF.Sqrt(mean);
            float db = 20 * MathF.Log10(amplitude + 1e-9f) + gains[b];
            float target = live ? Math.Clamp((db + 70) / 60, 0, 1) : 0;      // −70 dB … −10 dB → 0 … 1
            float previous = Bands[b];
            Bands[b] = previous + (target - previous) * (target > previous ? 0.6f : 0.18f);
        }
        IsLive = live;
    }
#else
    public void Start() { }
    public void Stop() { }
#endif
}

/// Equalizer bars drawn directly (no bindings), ~30 fps only while visible and music plays.
/// Without live audio (option off, no permission) the bars are a gentle animation, like the Mac's fallback.
public sealed class SpectrumBars : Control
{
    static readonly IBrush Bar = new SolidColorBrush(Color.Parse("#F2BD85"));
    static readonly IBrush Dim = new SolidColorBrush(Color.FromArgb(70, 242, 189, 133));
    readonly DispatcherTimer timer;

    public SpectrumBars()
    {
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => InvalidateVisual());
        AttachedToVisualTree += (_, _) => { Hub.Media.PlayingChanged += Update; Update(); };
        DetachedFromVisualTree += (_, _) => { Hub.Media.PlayingChanged -= Update; timer.Stop(); };
    }

    void Update()
    {
        if (Hub.Media.IsPlaying) timer.Start(); else { timer.Stop(); InvalidateVisual(); }
    }

    public override void Render(DrawingContext context)
    {
        int n = AudioSpectrum.BandCount;
        double gap = 4, w = (Bounds.Width - gap * (n - 1)) / n, h = Bounds.Height;
        var bands = Hub.Spectrum.Bands;
        bool live = Hub.Spectrum.IsLive, playing = Hub.Media.IsPlaying;
        double t = Environment.TickCount64 / 1000.0;
        for (int i = 0; i < n; i++)
        {
            double level = !playing ? 0
                : live ? bands[i]
                : 0.25 + 0.2 * Math.Sin(t * 3 + i * 0.6) * Math.Sin(t * 1.3 + i * 0.25);     // fallback animation
            double barH = Math.Max(w, h * Math.Clamp(level, 0, 1));
            context.DrawRectangle(level > 0.02 ? Bar : Dim, null, new Rect(i * (w + gap), (h - barH) / 2, w, barH), w / 2, w / 2);
        }
    }
}
