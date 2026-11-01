#if DEBUG
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;

namespace HaloNotch;

/// Debug only: HALONOTCH_SNAPSHOT=<dir> renders every tab of the first notch to PNG and quits.
/// Lets layouts be checked on machines without screen-recording permission (the app renders its own visuals).
static class Snapshot
{
    public static async void Run(NotchWindow window, string dir)
    {
        Directory.CreateDirectory(dir);
        window.OpenFor(TimeSpan.FromHours(1));
        foreach (var tab in Enum.GetValues<NotchTab>())
        {
            window.Select(tab);
            await Task.Delay(Environment.GetEnvironmentVariable("HALONOTCH_SNAPSHOT_DELAY") is { } d ? int.Parse(d) : 1500);
            var size = new PixelSize((int)(window.Bounds.Width * 2), (int)(window.Bounds.Height * 2));
            using var bitmap = new RenderTargetBitmap(size, new Vector(192, 192));
            bitmap.Render(window);
            bitmap.Save(Path.Combine(dir, $"{tab}.png"));
            Console.WriteLine($"SHOT {tab}");
        }
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }
}
#endif
