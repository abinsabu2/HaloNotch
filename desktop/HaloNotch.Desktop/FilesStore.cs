using System.Diagnostics;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
#if WINDOWS
using HaloNotch.Win;
#endif

namespace HaloNotch;

public sealed class FileEntry(string path, DateTime date)
{
    public string Path { get; } = path;
    public DateTime Date { get; } = date;
    public string Name => System.IO.Path.GetFileName(Path);
    public string Folder => System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(Path) ?? "");
    public bool IsImage => ClipItem.IsMediaFile(Path) && !IsVideoOrAudio;
    bool IsVideoOrAudio => System.IO.Path.GetExtension(Path).ToLowerInvariant() is ".mp4" or ".mov" or ".m4v" or ".avi" or ".mkv" or ".webm" or ".mp3" or ".wav" or ".m4a" or ".aac" or ".flac" or ".ogg";
    public string Glyph => Directory.Exists(Path) ? "" : IsVideoOrAudio ? "" : "";

    Bitmap? thumbnail;
    bool tried;
    /// Small preview for images (decoded once, downsampled).
    public Bitmap? Thumbnail
    {
        get
        {
            if (tried || !IsImage) return thumbnail;
            tried = true;
            try { using var s = File.OpenRead(Path); thumbnail = Bitmap.DecodeToWidth(s, 200); }
            catch (Exception) { thumbnail = null; }     // unreadable / unsupported image – show the glyph
            return thumbnail;
        }
    }
}

/// Files dropped onto the notch. Only references are kept – the originals are not copied or moved.
public sealed class FileTrayStore : Observable
{
    public List<FileEntry> Files { get; private set; } = [];
    public bool IsEmpty => Files.Count == 0;
    public string CountText => Loc.LF("%d files", Files.Count);

    public void Add(IEnumerable<string> paths)
    {
        Files = [.. Files, .. paths.Where(p => (File.Exists(p) || Directory.Exists(p)) && Files.All(f => f.Path != p)).Select(p => new FileEntry(p, DateTime.Now))];
        ChangedAll();
    }

    public void Remove(FileEntry file) { Files = Files.Where(f => f != file).ToList(); ChangedAll(); }
    public void Clear() { Files = []; ChangedAll(); }

    /// Drops entries whose file was moved or deleted in the meantime.
    public void PruneMissing()
    {
        var kept = Files.Where(f => File.Exists(f.Path) || Directory.Exists(f.Path)).ToList();
        if (kept.Count != Files.Count) { Files = kept; ChangedAll(); }
    }

    public void Open(FileEntry file) => Shell.Open(file.Path);
    public void Reveal(FileEntry file) => Shell.Reveal(file.Path);

    /// Puts the files on the clipboard (paste into Explorer, mail, chat).
    public void Copy(IEnumerable<FileEntry> files)
    {
#if WINDOWS
        ClipboardWin.WriteFiles(files.Select(f => f.Path).ToArray());
#endif
    }
}

/// Latest screenshots (last 24 h) from Pictures\Screenshots – where Win+PrtScn and the Snipping Tool save.
public sealed class ScreenshotStore : Observable
{
    FileSystemWatcher? watcher;
    IDisposable? rescan;

    public List<FileEntry> Shots { get; private set; } = [];
    public bool IsEmpty => Shots.Count == 0;

    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");

    public void Start()
    {
        Scan();
        if (!Directory.Exists(Folder)) return;
        watcher = new FileSystemWatcher(Folder) { EnableRaisingEvents = true, IncludeSubdirectories = false };
        watcher.Created += (_, _) => ScheduleScan();
        watcher.Deleted += (_, _) => ScheduleScan();
        watcher.Renamed += (_, _) => ScheduleScan();
    }

    /// Watcher events arrive on a worker thread, often several per file – scan once, shortly after.
    void ScheduleScan() => Dispatcher.UIThread.Post(() =>
    {
        rescan?.Dispose();
        rescan = DispatcherTimer.RunOnce(Scan, TimeSpan.FromMilliseconds(500));
    });

    void Scan()
    {
        var cutoff = DateTime.Now.AddDays(-1);
        try
        {
            Shots = Directory.Exists(Folder)
                ? new DirectoryInfo(Folder).EnumerateFiles()
                    .Where(f => f.Extension.ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" && f.CreationTime >= cutoff)
                    .OrderByDescending(f => f.CreationTime).Take(12)
                    .Select(f => Shots.FirstOrDefault(s => s.Path == f.FullName) ?? new FileEntry(f.FullName, f.CreationTime)).ToList()
                : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Debug.WriteLine($"HaloNotch: screenshot scan failed – {e.Message}"); }
        ChangedAll();
    }

    public void Copy(FileEntry shot)
    {
#if WINDOWS
        ClipboardWin.WriteFiles([shot.Path]);
#endif
    }

    public void Reveal(FileEntry shot) => Shell.Reveal(shot.Path);
    public void Open(FileEntry shot) => Shell.Open(shot.Path);

    /// Moves the screenshot to the Recycle Bin.
    public void Trash(FileEntry shot)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(shot.Path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                                                                   Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException) { return; }
        Shots = Shots.Where(s => s != shot).ToList();
        ChangedAll();
    }
}
