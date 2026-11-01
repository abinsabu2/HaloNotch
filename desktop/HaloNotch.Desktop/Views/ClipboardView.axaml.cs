using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using static HaloNotch.Loc;

namespace HaloNotch.Views;

/// Clipboard tab: search + one column per kind (Text, Code, Links, Files, Media, Colors).
public partial class ClipboardView : UserControl
{
    public static string CopyText => L("Copy");
    public static string RemoveText => L("Remove");
    public static string VaultText => L("Move to Vault");

    readonly NotchWindow window;
    readonly Func<Control>? makeVault;

    public ClipboardView() : this(null!, null) { }

    public ClipboardView(NotchWindow window, Func<Control>? makeVault)
    {
        InitializeComponent();
        this.window = window;
        this.makeVault = makeVault;
        DataContext = Hub.Clipboard;
        Search.PlaceholderText = L("Search");
        VaultLabel.Text = L("Vault");
        VaultButton.IsVisible = makeVault != null;
        KeyboardHintText.Text = L("Press 1–0 to paste · Esc to close");

        (string Title, string Path)[] columns =
        [
            (L("Text"), nameof(ClipboardStore.TextItems)), (L("Code"), nameof(ClipboardStore.CodeItems)),
            (L("Links"), nameof(ClipboardStore.LinkItems)), (L("Files"), nameof(ClipboardStore.FileItems)),
            (L("Media"), nameof(ClipboardStore.MediaItems)), (L("Colors"), nameof(ClipboardStore.ColorItems)),
        ];
        var template = (IDataTemplate)Resources["Entry"]!;
        for (int i = 0; i < columns.Length; i++)
        {
            var (title, path) = columns[i];
            ColumnGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var list = new ItemsControl { ItemTemplate = template };
            list.Bind(ItemsControl.ItemsSourceProperty, new Binding(path));
            var count = new TextBlock { Classes = { "tertiary", "small" }, FontWeight = Avalonia.Media.FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Right };
            count.Bind(TextBlock.TextProperty, new Binding(path + ".Count") { Converter = Conv.CountOrEmpty });
            var empty = new TextBlock { Text = "–", Classes = { "tertiary" }, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            empty.Bind(IsVisibleProperty, new Binding(path + ".Count") { Converter = Conv.IsZero });
            var column = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*"),
                Children =
                {
                    new Grid { Margin = new(10, 8, 10, 6), Children = { new TextBlock { Text = title, FontWeight = Avalonia.Media.FontWeight.SemiBold, FontSize = 12 }, count } },
                    empty,
                    new ScrollViewer { Content = list, Padding = new(6, 0, 6, 6) },
                },
            };
            Grid.SetRow(column.Children[1], 1);
            Grid.SetRow(column.Children[2], 1);
            Grid.SetColumn(column, i);
            if (path == nameof(ClipboardStore.ColorItems))
            {
                // Hidden column must not keep its share of the width.
                var definition = ColumnGrid.ColumnDefinitions[i];
                void Update() { column.IsVisible = Hub.Clipboard.HasColors; definition.Width = Hub.Clipboard.HasColors ? GridLength.Star : new GridLength(0); }
                Hub.Clipboard.PropertyChanged += (_, _) => Update();
                Update();
            }
            if (i > 0) column.Children.Add(new Border { Classes = { "vline" }, HorizontalAlignment = HorizontalAlignment.Left, [Grid.RowSpanProperty] = 2 });
            ColumnGrid.Children.Add(column);
        }

        if (window != null)
        {
            window.KeyboardModeChanged += () => KeyboardHint.IsVisible = window.KeyboardMode;
            KeyboardHint.IsVisible = window.KeyboardMode;
        }
    }

    static ClipItem? Item(object? sender) => (sender as Control)?.DataContext as ClipItem;

    void OnCopy(object? sender, RoutedEventArgs e) { if (Item(sender) is { } item) Hub.Clipboard.Copy(item); }
    void OnPin(object? sender, RoutedEventArgs e) { if (Item(sender) is { } item) Hub.Clipboard.TogglePin(item); }
    void OnRemove(object? sender, RoutedEventArgs e) { if (Item(sender) is { } item) Hub.Clipboard.Remove(item); }
    void OnMenuOpened(object? sender, RoutedEventArgs e) => Popups.Open++;
    void OnMenuClosed(object? sender, RoutedEventArgs e) => Popups.Open = Math.Max(0, Popups.Open - 1);

    void OnMoveToVault(object? sender, RoutedEventArgs e)
    {
        if (Item(sender) is not { Text: { } text } item) return;
        Hub.Vault.MoveIn(text, () => Hub.Clipboard.Remove(item));
        ShowVault(true);
    }

    void OnToggleVault(object? sender, RoutedEventArgs e) => ShowVault(!VaultHost.IsVisible);

    void ShowVault(bool show)
    {
        if (makeVault == null) return;
        VaultHost.Content ??= makeVault();
        VaultHost.IsVisible = show;
        Columns.IsVisible = !show;
        Search.IsVisible = !show;
        VaultButton.Classes.Set("on", show);
    }
}
