#pragma warning disable CS0649, CS0414, CS0067 // some fields/events are only used by the Windows build
using System.Diagnostics;
using Avalonia.Threading;
#if WINDOWS
using System.Management;
using NAudio.CoreAudioApi;
#endif

namespace HaloNotch;

public sealed record HudEvent(bool IsVolume, double Level, bool Muted);

/// Output volume (Core Audio) and built-in display brightness (WMI – laptops only; external monitors
/// would need DDC/CI). Raises HUD events on external changes and lets the panel sliders set both.
/// Windows still shows its own volume/brightness flyout as well (like the system HUD on the Mac).
public sealed class SystemControls : Observable
{
    double volume, brightness;
    bool muted, volumeAvailable, brightnessAvailable;

    /// Two-way bound by the sliders.
    public double Volume { get => volume; set => SetVolume(value); }
    public bool Muted => muted;
    public bool VolumeAvailable => volumeAvailable;
    public double Brightness { get => brightness; set => SetBrightness(value); }
    public bool BrightnessAvailable => brightnessAvailable;
    public string VolumeText => volumeAvailable ? $"{Math.Round(volume * 100)}" : "–";
    public string BrightnessText => brightnessAvailable ? $"{Math.Round(brightness * 100)}" : "–";
    public string VolumeGlyph => muted || volume == 0 ? "" : "";

    public event Action<HudEvent>? Event;

#if WINDOWS
    readonly MMDeviceEnumerator enumerator = new();
    MMDevice? device;
    MMDeviceNotificationClient? notifier;
    ManagementEventWatcher? brightnessWatcher;

    public void Start()
    {
        try
        {
            // Re-attach when the default output changes (headphones plugged in, …).
            notifier = enumerator.CreateNotificationClient(false);
            notifier.DefaultDeviceChanged += (_, e) =>
            {
                if (e.Flow == DataFlow.Render && e.Role == Role.Multimedia) Dispatcher.UIThread.Post(Attach);
            };
            Attach();
        }
        catch (Exception e) { Debug.WriteLine($"HaloNotch: volume unavailable – {e.Message}"); }
        StartBrightness();
    }

    void Attach()
    {
        if (device != null) device.AudioEndpointVolume.OnVolumeNotification -= OnVolume;
        try
        {
            device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            device.AudioEndpointVolume.OnVolumeNotification += OnVolume;
            volume = device.AudioEndpointVolume.MasterVolumeLevelScalar;
            muted = device.AudioEndpointVolume.Mute;
            volumeAvailable = true;
        }
        catch (Exception) { device = null; volumeAvailable = false; }     // no output device
        ChangedAll();
    }

    void OnVolume(AudioVolumeNotificationData data) => Dispatcher.UIThread.Post(() =>
    {
        if (Math.Abs(data.MasterVolume - volume) <= 0.001 && data.Muted == muted) return;
        volume = data.MasterVolume;
        muted = data.Muted;
        ChangedAll();
        Event?.Invoke(new HudEvent(true, volume, muted));
    });

    /// Sets the output volume (0…1); unmutes when raised above 0.
    public void SetVolume(double value)
    {
        if (device == null) return;
        volume = Math.Clamp(value, 0, 1);       // update first so the notification isn't treated as external
        if (muted && volume > 0) { muted = false; device.AudioEndpointVolume.Mute = false; }
        device.AudioEndpointVolume.MasterVolumeLevelScalar = (float)volume;
        ChangedAll();
    }

    public void ToggleMute()
    {
        if (device == null) return;
        muted = !muted;
        device.AudioEndpointVolume.Mute = muted;
        ChangedAll();
    }

    void StartBrightness()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            foreach (ManagementObject o in searcher.Get())
            {
                brightness = Convert.ToDouble(o["CurrentBrightness"]) / 100;
                brightnessAvailable = true;
            }
            if (!brightnessAvailable) return;
            brightnessWatcher = new ManagementEventWatcher(new ManagementScope(@"root\WMI"),
                new WqlEventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
            brightnessWatcher.EventArrived += (_, e) =>
            {
                var level = Convert.ToDouble(e.NewEvent["Brightness"]) / 100;
                Dispatcher.UIThread.Post(() =>
                {
                    if (Math.Abs(level - brightness) < 0.01) return;
                    brightness = level;
                    ChangedAll();
                    Event?.Invoke(new HudEvent(false, level, false));
                });
            };
            brightnessWatcher.Start();
        }
        catch (Exception e) { Debug.WriteLine($"HaloNotch: brightness unavailable – {e.Message}"); }   // desktop PC / external monitor
        ChangedAll();
    }

    /// Sets the built-in display brightness (0…1).
    public void SetBrightness(double value)
    {
        if (!brightnessAvailable) return;
        brightness = Math.Clamp(value, 0, 1);
        ChangedAll();
        var level = (byte)Math.Round(brightness * 100);
        Task.Run(() =>                               // WMI calls can take ~100 ms – keep the slider smooth
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
                foreach (ManagementObject o in searcher.Get()) o.InvokeMethod("WmiSetBrightness", [(uint)1, level]);
            }
            catch (Exception e) { Debug.WriteLine($"HaloNotch: could not set brightness – {e.Message}"); }
        });
    }

#else
    public void Start() { }
    public void SetVolume(double value) { volume = value; ChangedAll(); }
    public void ToggleMute() { }
    public void SetBrightness(double value) { brightness = value; ChangedAll(); }
#endif
}
