using System.Text;
using System.Text.Json;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
#if WINDOWS
using System.Security.Cryptography;
using HaloNotch.Win;
#endif

namespace HaloNotch;

/// Clipboard history: the 50 most recent text/image/file items, in memory only. Pinned texts survive restarts
/// (encrypted for the Windows user with DPAPI – the counterpart of the Mac's keychain).
public sealed class ClipboardStore : Observable
{
    const int MaxItems = 50, MaxImages = 10;

    /// Apps whose copies are never stored (matched against the executable path, case-insensitive).
    static readonly string[] PasswordManagerHints =
        ["enpass", "1password", "bitwarden", "keepass", "lastpass", "dashlane", "nordpass", "proton pass", "protonpass", "roboform"];

    readonly List<ClipItem> items = [];
    string query = "";
#if WINDOWS
    uint lastSequence;
#endif

    public IReadOnlyList<ClipItem> Items => items;
    public int Count => items.Count;
    public string CountText => Loc.LF("%d of %d", items.Count, MaxItems);

    public string Query { get => query; set { if (Set(ref query, value)) ChangedAll(); } }

    IEnumerable<ClipItem> Filtered =>
        string.IsNullOrWhiteSpace(query) ? items
        : items.Where(i => i.SearchText.Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase));

    public List<ClipItem> TextItems => Filtered.Where(i => i.Kind == ClipKind.Text).ToList();
    public List<ClipItem> CodeItems => Filtered.Where(i => i.Kind == ClipKind.Code).ToList();
    public List<ClipItem> LinkItems => Filtered.Where(i => i.Kind == ClipKind.Link).ToList();
    public List<ClipItem> FileItems => Filtered.Where(i => i.Kind == ClipKind.File).ToList();
    public List<ClipItem> MediaItems => Filtered.Where(i => i.Kind == ClipKind.Media).ToList();
    public List<ClipItem> ColorItems => Filtered.Where(i => i.Kind == ClipKind.Color).ToList();
    /// The Colors column appears once a colour was copied (checked on the full history, so it doesn't jump while searching).
    public bool HasColors => items.Any(i => i.Kind == ClipKind.Color);
    public List<ClipItem> Recent => items.Take(4).ToList();

    static string PinnedPath => Path.Combine(Prefs.Dir, "pinned.bin");

    public ClipboardStore()
    {
        foreach (var text in LoadPinned()) items.Add(new ClipItem(text, isPinned: true));
        Publish();
    }

    public void Start()
    {
#if WINDOWS
        lastSequence = ClipboardWin.Sequence;
        MessageWindow.Shared.ClipboardChanged += Capture;
#endif
    }

#if WINDOWS
    void Capture()
    {
        var sequence = ClipboardWin.Sequence;
        if (sequence == lastSequence) return;           // our own write
        lastSequence = sequence;
        if (Prefs.Get(Prefs.IgnorePasswordManagers, true) && FromPasswordManager()) return;
        var content = ClipboardWin.Read();
        if (content == null) return;
        if (content.Files is { Length: > 0 } files)
            Insert(new ClipItem(files: files, image: files.Length == 1 && ClipItem.IsMediaFile(files[0]) ? Thumbnail(files[0]) : null));
        else if (!string.IsNullOrWhiteSpace(content.Text))
            Insert(new ClipItem(content.Text));
        else if (content.Dib != null && Decode(ClipboardWin.DibToBmp(content.Dib)) is { } image)
            Insert(new ClipItem(dib: content.Dib, image: image));
    }

    static bool FromPasswordManager()
    {
        var path = Native.ProcessPath(Native.GetForegroundWindow())?.ToLowerInvariant() ?? "";
        return PasswordManagerHints.Any(path.Contains);
    }
#endif

    static Bitmap? Decode(byte[] data)
    {
        try { using var s = new MemoryStream(data); return Bitmap.DecodeToWidth(s, 320); }
        catch (Exception) { return null; }      // unsupported / corrupt image – keep nothing rather than crash
    }

    static Bitmap? Thumbnail(string path)
    {
        try { using var s = File.OpenRead(path); return Bitmap.DecodeToWidth(s, 160); }
        catch (Exception) { return null; }
    }

    void Insert(ClipItem item)
    {
        if (item.Text != null)
        {
            if (items.Any(i => i.IsPinned && i.Text == item.Text)) return;     // already kept as a pinned item
            items.RemoveAll(i => !i.IsPinned && i.Text == item.Text);
        }
        if (item.Files != null)
            items.RemoveAll(i => !i.IsPinned && i.Files != null && i.Files.SequenceEqual(item.Files));
        items.Insert(0, item);
        // Keep at most MaxItems / MaxImages; pinned items are never dropped (oldest unpinned go first).
        while (items.Count > MaxItems && items.FindLastIndex(i => !i.IsPinned) is var last and >= 0) items.RemoveAt(last);
        while (items.Count(i => i.IsImage && i.Files == null && !i.IsPinned) > MaxImages)
            items.RemoveAt(items.FindLastIndex(i => i.IsImage && i.Files == null && !i.IsPinned));
        Publish();
    }

    void Publish()
    {
        for (int i = 0; i < items.Count; i++) items[i].Shortcut = i < 10 ? ((i + 1) % 10).ToString() : null;
        ChangedAll();
    }

    /// Puts the item back on the clipboard (user then presses Ctrl+V).
    public void Copy(ClipItem item)
    {
#if WINDOWS
        if (item.Text != null) ClipboardWin.WriteText(item.Text);
        else if (item.Files != null) ClipboardWin.WriteFiles(item.Files);
        else if (item.Dib != null) ClipboardWin.WriteDib(item.Dib);
        lastSequence = ClipboardWin.Sequence;          // don't re-capture our own write
#endif
        items.Remove(item);
        items.Insert(0, item);
        item.IsCopied = true;
        DispatcherTimer.RunOnce(() => item.IsCopied = false, TimeSpan.FromSeconds(1.2));
        Publish();
    }

    public void Remove(ClipItem item)
    {
        items.Remove(item);
        SavePinned();
        Publish();
    }

    public void TogglePin(ClipItem item)
    {
        item.IsPinned = !item.IsPinned;
        SavePinned();
        Publish();
    }

    /// Clears the history but keeps pinned items.
    public void Clear()
    {
        items.RemoveAll(i => !i.IsPinned);
        Publish();
    }

    // MARK: Pinned texts

    static List<string> LoadPinned()
    {
#if WINDOWS
        try
        {
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(PinnedPath), null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<List<string>>(plain) ?? [];
        }
        catch (Exception e) when (e is IOException or CryptographicException or JsonException or UnauthorizedAccessException) { }
#endif
        return [];      // ponytail: other platforms keep pins for the session only (Linux: libsecret later)
    }

    void SavePinned()
    {
#if WINDOWS
        var texts = items.Where(i => i.IsPinned && i.Text != null).Select(i => i.Text!).ToList();
        try
        {
            Directory.CreateDirectory(Prefs.Dir);
            var sealedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(texts)), null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(PinnedPath, sealedBytes);
        }
        catch (Exception e) when (e is IOException or CryptographicException or UnauthorizedAccessException) { }
#endif
    }
}
