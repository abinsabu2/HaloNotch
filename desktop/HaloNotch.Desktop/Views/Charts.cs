using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace HaloNotch.Views;

/// Donut of category shares (Screen Time).
public sealed class Donut : Control
{
    public static readonly StyledProperty<IReadOnlyList<CategoryUsage>?> SegmentsProperty =
        AvaloniaProperty.Register<Donut, IReadOnlyList<CategoryUsage>?>(nameof(Segments));
    public IReadOnlyList<CategoryUsage>? Segments { get => GetValue(SegmentsProperty); set => SetValue(SegmentsProperty, value); }
    public double Thickness { get; set; } = 14;
    static Donut() => AffectsRender<Donut>(SegmentsProperty);

    public override void Render(DrawingContext context)
    {
        double size = Math.Min(Bounds.Width, Bounds.Height), r = (size - Thickness) / 2;
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(26, 255, 255, 255)), Thickness), center, r, r);
        var segments = Segments ?? [];
        double total = segments.Sum(s => s.Seconds), angle = -90;
        if (total <= 0) return;
        foreach (var s in segments)
        {
            double sweep = s.Seconds / total * 360;
            if (sweep < 1) continue;
            context.DrawGeometry(null, new Pen(s.Color, Thickness, lineCap: PenLineCap.Round), Arc(center, r, angle + 4, Math.Max(sweep - 8, 0.5)));
            angle += sweep;
        }
    }

    internal static StreamGeometry Arc(Point c, double r, double startDeg, double sweepDeg)
    {
        Point P(double deg) => new(c.X + r * Math.Cos(deg * Math.PI / 180), c.Y + r * Math.Sin(deg * Math.PI / 180));
        var g = new StreamGeometry();
        using var ctx = g.Open();
        ctx.BeginFigure(P(startDeg), false);
        ctx.ArcTo(P(startDeg + sweepDeg), new Size(r, r), 0, sweepDeg > 180, SweepDirection.Clockwise);
        ctx.EndFigure(false);
        return g;
    }
}

/// Progress ring (focus timer).
public sealed class Ring : Control
{
    public static readonly StyledProperty<double> ProgressProperty = AvaloniaProperty.Register<Ring, double>(nameof(Progress));
    public double Progress { get => GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public double Thickness { get; set; } = 6;
    static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#F2BD85"));
    static Ring() => AffectsRender<Ring>(ProgressProperty);

    public override void Render(DrawingContext context)
    {
        double r = (Math.Min(Bounds.Width, Bounds.Height) - Thickness) / 2;
        var c = new Point(Bounds.Width / 2, Bounds.Height / 2);
        context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(26, 255, 255, 255)), Thickness), c, r, r);
        var p = Math.Clamp(Progress, 0, 1);
        if (p > 0.002) context.DrawGeometry(null, new Pen(Accent, Thickness, lineCap: PenLineCap.Round), Donut.Arc(c, r, -90, Math.Min(p * 360, 359.9)));
        double dot = -90 + p * 360;
        context.DrawEllipse(Accent, null, new Point(c.X + r * Math.Cos(dot * Math.PI / 180), c.Y + r * Math.Sin(dot * Math.PI / 180)), Thickness * 0.9, Thickness * 0.9);
    }
}

/// Activity by hour (24 bars, the current hour highlighted).
public sealed class HourBars : Control
{
    public static readonly StyledProperty<double[]?> ValuesProperty = AvaloniaProperty.Register<HourBars, double[]?>(nameof(Values));
    public double[]? Values { get => GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    static readonly IBrush Bar = new LinearGradientBrush
    {
        StartPoint = new(0, 0, RelativeUnit.Relative), EndPoint = new(0, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.Parse("#F2BD85"), 0), new GradientStop(Color.Parse("#E68C61"), 1) },
    };
    static readonly IBrush Empty = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255));
    static HourBars() => AffectsRender<HourBars>(ValuesProperty);

    public override void Render(DrawingContext context)
    {
        var v = Values ?? new double[24];
        double max = Math.Max(v.Max(), 1), gap = 2, w = (Bounds.Width - gap * 23) / 24;
        for (int i = 0; i < 24; i++)
        {
            double h = Math.Max(2, Bounds.Height * v[i] / max);
            context.DrawRectangle(v[i] > 0 ? Bar : Empty, null, new Rect(i * (w + gap), Bounds.Height - h, w, h), 2, 2);
        }
    }
}
