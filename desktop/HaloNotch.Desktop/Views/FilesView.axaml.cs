using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using static HaloNotch.Loc;

namespace HaloNotch.Views;

/// Files tab: file tray (drop onto the notch, drag back out, double-click to open) | screenshots shelf.
public partial class FilesView : UserControl
{
    public static string OpenText => L("Open");
    public static string RevealText => L("Show in Explorer");
    public static string CopyText => L("Copy");
    public static string RemoveText => L("Remove from Tray");
    public static string TrashText => L("Move to Recycle Bin");

    public FilesView()
    {
        InitializeComponent();
        DataContext = Hub.Files;
        DropHint.Text = L("Drop files on the notch to keep them here");
        ClearButton.Content = L("Clear tray");
        CopyAllButton.Content = L("Copy");
        ShotsTitle.Text = L("Screenshots").ToUpperInvariant();
        ShotsEmpty.Text = L("New screenshots appear here");
        SetupDragOut();
    }

    static FileEntry? Entry(object? sender) => (sender as Control)?.DataContext as FileEntry;

    void OnOpen(object? s, RoutedEventArgs e) { if (Entry(s) is { } f) Hub.Files.Open(f); }
    void OnOpenTapped(object? s, TappedEventArgs e) { if (Entry(s) is { } f) Hub.Files.Open(f); }
    void OnReveal(object? s, RoutedEventArgs e) { if (Entry(s) is { } f) Hub.Files.Reveal(f); }
    void OnCopy(object? s, RoutedEventArgs e) { if (Entry(s) is { } f) Hub.Files.Copy([f]); }
    void OnRemove(object? s, RoutedEventArgs e) { if (Entry(s) is { } f) Hub.Files.Remove(f); }
    void OnCopyAll(object? s, RoutedEventArgs e) => Hub.Files.Copy(Hub.Files.Files);
    void OnClear(object? s, RoutedEventArgs e) => Hub.Files.Clear();
    void OnShotOpen(object? s, RoutedEventArgs e) { if (Entry(s) is { } f) Hub.Screenshots.Open(f); }
    void OnShotCopy(object? s, RoutedEventArgs e) { if (Entry(s) is { } f) Hub.Screenshots.Copy(f); }
    void OnShotReveal(object? s, RoutedEventArgs e) { if (Entry(s) is { } f) Hub.Screenshots.Reveal(f); }
    void OnShotTrash(object? s, RoutedEventArgs e) { if (Entry(s) is { } f) Hub.Screenshots.Trash(f); }
    void OnMenuOpened(object? s, RoutedEventArgs e) => Popups.Open++;
    void OnMenuClosed(object? s, RoutedEventArgs e) => Popups.Open = Math.Max(0, Popups.Open - 1);

    Point pressPoint;
    FileEntry? pressed;
    PointerPressedEventArgs? pressArgs;

    /// Drag a tray file or screenshot out into another app / Explorer (starts after a small movement,
    /// so click and double-click keep working; buttons mark the press handled, hence handledEventsToo).
    void SetupDragOut()
    {
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            pressed = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ? (e.Source as StyledElement)?.DataContext as FileEntry : null;
            pressPoint = e.GetPosition(this);
            pressArgs = e;
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, (_, _) => pressed = null, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, async (_, e) =>
        {
            if (pressed is not { } entry || pressArgs is not { } press || Point.Distance(e.GetPosition(this), pressPoint) < 6) return;
            pressed = null;
            if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
            IStorageItem? item = Directory.Exists(entry.Path) ? await storage.TryGetFolderFromPathAsync(entry.Path) : await storage.TryGetFileFromPathAsync(entry.Path);
            if (item == null) return;
            var data = new DataTransfer();
            data.Add(DataTransferItem.CreateFile(item));
            await DragDrop.DoDragDropAsync(press, data, DragDropEffects.Copy | DragDropEffects.Link);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
    }
}
