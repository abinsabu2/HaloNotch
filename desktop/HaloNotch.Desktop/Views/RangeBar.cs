using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace HaloNotch;

/// Vertical temperature range bar (week view): a track with an accent segment from Start to Start+Length (0…1, bottom-up).
public class RangeBar : Control
{
    public static readonly StyledProperty<double> StartProperty = AvaloniaProperty.Register<RangeBar, double>(nameof(Start));
    public static readonly StyledProperty<double> LengthProperty = AvaloniaProperty.Register<RangeBar, double>(nameof(Length));
    public double Start { get => GetValue(StartProperty); set => SetValue(StartProperty, value); }
    public double Length { get => GetValue(LengthProperty); set => SetValue(LengthProperty, value); }

    static readonly IBrush Track = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
    static readonly IBrush Fill = new LinearGradientBrush
    {
        StartPoint = new(0, 0, RelativeUnit.Relative), EndPoint = new(0, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.Parse("#F2BD85"), 0), new GradientStop(Color.Parse("#66A8FF"), 1) },
    };

    static RangeBar() => AffectsRender<RangeBar>(StartProperty, LengthProperty);

    public override void Render(DrawingContext context)
    {
        var b = Bounds;
        double r = b.Width / 2;
        context.DrawRectangle(Track, null, new Rect(0, 0, b.Width, b.Height), r, r);
        double h = Math.Max(b.Height * Length, b.Width);
        double y = b.Height - b.Height * Start - h;
        context.DrawRectangle(Fill, null, new Rect(0, Math.Max(y, 0), b.Width, h), r, r);
    }
}
