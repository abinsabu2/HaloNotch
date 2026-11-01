using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using static HaloNotch.Loc;

namespace HaloNotch.Views;

/// Media tab: artwork, track, equalizer, progress (click to seek), transport, output volume.
public partial class MediaView : UserControl
{
    readonly DispatcherTimer ticker;
    MediaController Media => Hub.Media;

    public MediaView() : this(null) { }

    public MediaView(Control? equalizer)
    {
        InitializeComponent();
        DataContext = Hub.Media;
        EqualizerHost.Content = equalizer;
        OutputTitle.Text = L("Output");
        ticker = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Media.Tick());
        AttachedToVisualTree += (_, _) => ticker.Start();
        DetachedFromVisualTree += (_, _) => ticker.Stop();
        Hub.Controls.PropertyChanged += (_, _) => UpdateTexts();
        Media.PropertyChanged += (_, _) => UpdateTexts();
        UpdateTexts();
    }

    void UpdateTexts()
    {
        VolumeLabel.Text = Hub.Controls.VolumeAvailable ? $"{L("Volume")} · {Hub.Controls.VolumeText} %" : L("Volume unavailable");
        OpenPlayerButton.Content = Media.Now is { } n ? LF("Open %@", n.App) : L("Nothing playing");
        OpenPlayerButton.IsEnabled = Media.HasMedia;
    }

    void OnSeek(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control bar && bar.Bounds.Width > 0) Media.Seek(e.GetPosition(bar).X / bar.Bounds.Width);
    }

    void OnPlayPause(object? sender, RoutedEventArgs e) => Media.PlayPause();
    void OnNext(object? sender, RoutedEventArgs e) => Media.Next();
    void OnPrevious(object? sender, RoutedEventArgs e) => Media.Previous();
    void OnShuffle(object? sender, RoutedEventArgs e) => Media.ToggleShuffle();
    void OnRepeat(object? sender, RoutedEventArgs e) => Media.ToggleRepeat();
    void OnOpenPlayer(object? sender, RoutedEventArgs e) => Media.OpenPlayer();
}
