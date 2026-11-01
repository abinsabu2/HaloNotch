#if DEBUG
#pragma warning disable CS0618 // Bitmap.Save(path) – debug tool only
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
        if (Environment.GetEnvironmentVariable("HALONOTCH_SNAPSHOT_SAMPLE") != null) await Sample();
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
        var settings = new SettingsWindow();
        settings.Show();
        for (int i = 0; i < 5; i++)
        {
            settings.ShowTab(i);
            await Task.Delay(800);
            using var shot = new RenderTargetBitmap(new PixelSize((int)settings.Bounds.Width * 2, (int)settings.Bounds.Height * 2), new Vector(192, 192));
            shot.Render(settings);
            shot.Save(Path.Combine(dir, $"Settings{i}.png"));
        }
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    /// Sample data for the filled states + a real vault create/lock/unlock round trip (files in HALONOTCH_DATA).
    static async Task Sample()
    {
        var c = Hub.Clipboard;
        c.AddForTest(new ClipItem("Meeting notes: send the deck to Anna before Friday."));
        c.AddForTest(new ClipItem("const total = items.reduce((sum, i) => sum + i.price, 0);\nreturn total;"));
        c.AddForTest(new ClipItem("https://github.com/abinsabu2/HaloNotch"));
        c.AddForTest(new ClipItem("#F2BD85"));
        c.AddForTest(new ClipItem("oklch(70% 0.15 50)"));
        var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        c.AddForTest(new ClipItem(files: [Path.Combine(repo, "README.md")]));
        var shot = Path.Combine(repo, "docs/screenshots/weather.png");
        c.AddForTest(new ClipItem(files: [shot], image: Avalonia.Media.Imaging.Bitmap.DecodeToWidth(File.OpenRead(shot), 160)));
        c.TogglePin(c.Items.First(i => i.Kind == ClipKind.Text));
        Hub.Files.Add([Path.Combine(repo, "README.md"), shot, Path.Combine(repo, "desktop")]);
        Hub.Focus.Start();

        var now = DateTime.Now;
        string T(DateTime t) => t.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'");
        Hub.Calendar.LoadForTest("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:test\r\n" +
            $"BEGIN:VEVENT\r\nUID:a\r\nDTSTART:{T(now.AddMinutes(30))}\r\nDTEND:{T(now.AddMinutes(60))}\r\nSUMMARY:Design review\r\nLOCATION:https://teams.microsoft.com/l/meetup-join/x\r\nEND:VEVENT\r\n" +
            $"BEGIN:VEVENT\r\nUID:b\r\nDTSTART:{T(now.Date.AddDays(1).AddHours(9))}\r\nDTEND:{T(now.Date.AddDays(1).AddHours(9.25))}\r\nRRULE:FREQ=DAILY;COUNT=5\r\nSUMMARY:Standup\r\nEND:VEVENT\r\n" +
            $"BEGIN:VEVENT\r\nUID:c\r\nDTSTART;VALUE=DATE:{now.Date.AddDays(3):yyyyMMdd}\r\nDTEND;VALUE=DATE:{now.Date.AddDays(4):yyyyMMdd}\r\nSUMMARY:Holiday\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n");

        var v = Hub.Vault;
        if (v.IsSetup) { v.Passcode = v.Confirm = "123456"; v.Create(); }
        await Until(() => v.IsUnlocked);
        v.DraftLabel = "Wi-Fi"; v.DraftSecret = "s3cret-pass"; v.Add();
        v.Lock();
        v.Passcode = "000000"; v.Unlock();
        await Until(() => !v.Busy);
        bool wrongRejected = v.IsLocked && v.HasError;
        v.Passcode = "123456"; v.Unlock();
        await Until(() => v.IsUnlocked);
        Console.WriteLine($"VAULT wrongRejected={wrongRejected} reopened={v.IsUnlocked} entries={v.Entries.Count} first={v.Entries.FirstOrDefault()?.Secret}");
        Console.WriteLine($"CLIP items={c.Count} text={c.TextItems.Count} code={c.CodeItems.Count} links={c.LinkItems.Count} files={c.FileItems.Count} media={c.MediaItems.Count} colors={c.ColorItems.Count} pinned={c.Items.Count(i => i.IsPinned)}");
        Console.WriteLine($"CAL upcoming={Hub.Calendar.Upcoming.Count} next={Hub.Calendar.Next?.Title} join={Hub.Calendar.Next?.CanJoin} days={Hub.Calendar.EventDays.Count}");
    }

    static async Task Until(Func<bool> done)
    {
        for (int i = 0; i < 100 && !done(); i++) await Task.Delay(100);
    }
}
#endif
