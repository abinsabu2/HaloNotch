using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace HaloNotch.Views;

/// TextBlock showing a ClipItem's syntax-coloured runs (made once at capture).
public class HighlightedText : TextBlock
{
    protected override Type StyleKeyOverride => typeof(TextBlock);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != DataContextProperty) return;
        Inlines ??= [];
        Inlines.Clear();
        if (DataContext is ClipItem { Highlighted: { } runs })
            foreach (var (text, color) in runs) Inlines.Add(new Run(text) { Foreground = new SolidColorBrush(color) });
    }
}
