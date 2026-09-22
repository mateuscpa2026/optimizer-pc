using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace OptimizerPC.App.Controls;

/// <summary>
/// Medidor em arco. Usado no indice de saude e nos cartoes de desempenho.
/// O valor exibido e animado para que a leitura nao "salte".
/// </summary>
public sealed class GaugeRing : FrameworkElement
{
    private const double StartAngle = 135d;
    private const double SweepAngle = 270d;

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value),
        typeof(double),
        typeof(GaugeRing),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender, OnValueChanged));

    public static readonly DependencyProperty AnimatedValueProperty = DependencyProperty.Register(
        nameof(AnimatedValue),
        typeof(double),
        typeof(GaugeRing),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum),
        typeof(double),
        typeof(GaugeRing),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum),
        typeof(double),
        typeof(GaugeRing),
        new FrameworkPropertyMetadata(100d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness),
        typeof(double),
        typeof(GaugeRing),
        new FrameworkPropertyMetadata(11d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush),
        typeof(Brush),
        typeof(GaugeRing),
        new FrameworkPropertyMetadata(
            new SolidColorBrush(Color.FromRgb(0x22, 0x30, 0x4C)),
            FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ProgressBrushProperty = DependencyProperty.Register(
        nameof(ProgressBrush),
        typeof(Brush),
        typeof(GaugeRing),
        new FrameworkPropertyMetadata(
            new SolidColorBrush(Color.FromRgb(0x2A, 0xC7, 0xEA)),
            FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label),
        typeof(string),
        typeof(GaugeRing),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SubLabelProperty = DependencyProperty.Register(
        nameof(SubLabel),
        typeof(string),
        typeof(GaugeRing),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(
        nameof(TextBrush),
        typeof(Brush),
        typeof(GaugeRing),
        new FrameworkPropertyMetadata(
            new SolidColorBrush(Color.FromRgb(0xF3, 0xF7, 0xFC)),
            FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SubTextBrushProperty = DependencyProperty.Register(
        nameof(SubTextBrush),
        typeof(Brush),
        typeof(GaugeRing),
        new FrameworkPropertyMetadata(
            new SolidColorBrush(Color.FromRgb(0x8E, 0xA3, 0xC4)),
            FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LabelSizeProperty = DependencyProperty.Register(
        nameof(LabelSize),
        typeof(double),
        typeof(GaugeRing),
        new FrameworkPropertyMetadata(30d, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>Valor efetivamente desenhado (interpolado).</summary>
    public double AnimatedValue
    {
        get => (double)GetValue(AnimatedValueProperty);
        private set => SetValue(AnimatedValueProperty, value);
    }

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public Brush ProgressBrush
    {
        get => (Brush)GetValue(ProgressBrushProperty);
        set => SetValue(ProgressBrushProperty, value);
    }

    public string? Label
    {
        get => (string?)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string? SubLabel
    {
        get => (string?)GetValue(SubLabelProperty);
        set => SetValue(SubLabelProperty, value);
    }

    public Brush TextBrush
    {
        get => (Brush)GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
    }

    public Brush SubTextBrush
    {
        get => (Brush)GetValue(SubTextBrushProperty);
        set => SetValue(SubTextBrushProperty, value);
    }

    public double LabelSize
    {
        get => (double)GetValue(LabelSizeProperty);
        set => SetValue(LabelSizeProperty, value);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var gauge = (GaugeRing)d;
        var target = gauge.Normalize((double)e.NewValue);
        var current = gauge.AnimatedValue;

        if (!Motion.Enabled || gauge.IsLoaded is false)
        {
            gauge.AnimatedValue = target;
            return;
        }

        var animation = new DoubleAnimation
        {
            From = current,
            To = target,
            Duration = TimeSpan.FromMilliseconds(520),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };

        gauge.AnimatedValue = target;
        gauge.BeginAnimation(AnimatedValueProperty, animation);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 2 || height <= 2)
        {
            return;
        }

        var thickness = Math.Max(1d, Thickness);
        var size = Math.Min(width, height);
        var radius = (size - thickness) / 2d - 1d;
        if (radius <= 1d)
        {
            return;
        }

        var center = new Point(width / 2d, height / 2d);
        DrawArc(drawingContext, center, radius, StartAngle, StartAngle + SweepAngle, TrackBrush, thickness);

        var ratio = (AnimatedValue - Minimum) / Math.Max(1e-6, Maximum - Minimum);
        ratio = Math.Clamp(ratio, 0d, 1d);
        var valueEnd = StartAngle + (SweepAngle * ratio);

        if (ratio > 0.002d)
        {
            DrawArc(drawingContext, center, radius, StartAngle, valueEnd, ProgressBrush, thickness);
            var tip = PointOnCircle(center, radius, valueEnd);
            drawingContext.DrawEllipse(
                ProgressBrush,
                null,
                tip,
                Math.Max(1.5d, thickness * 0.16d),
                Math.Max(1.5d, thickness * 0.16d));
        }

        DrawTexts(drawingContext, center, radius);
    }

    private void DrawTexts(DrawingContext drawingContext, Point center, double radius)
    {
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        if (!string.IsNullOrWhiteSpace(Label))
        {
            var formatted = new FormattedText(
                Label!,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection,
                new Typeface("Segoe UI Variable Display, Segoe UI Semibold, Segoe UI"),
                Math.Max(12d, Math.Min(LabelSize, radius * 0.9d)),
                TextBrush,
                dpi)
            {
                TextAlignment = TextAlignment.Center
            };

            drawingContext.DrawText(formatted, new Point(center.X, center.Y - (formatted.Height / 2d) - 4d));
        }

        if (!string.IsNullOrWhiteSpace(SubLabel))
        {
            var formatted = new FormattedText(
                SubLabel!,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection,
                new Typeface("Segoe UI Variable Text, Segoe UI"),
                Math.Max(9d, Math.Min(LabelSize * 0.38d, radius * 0.4d)),
                SubTextBrush,
                dpi)
            {
                TextAlignment = TextAlignment.Center
            };

            drawingContext.DrawText(formatted, new Point(center.X, center.Y + (radius * 0.34d)));
        }
    }

    private double Normalize(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return Minimum;
        }

        return Math.Clamp(value, Minimum, Maximum);
    }

    private static void DrawArc(
        DrawingContext drawingContext,
        Point center,
        double radius,
        double startAngle,
        double endAngle,
        Brush brush,
        double thickness)
    {
        if (endAngle - startAngle <= 0.01d)
        {
            return;
        }

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(PointOnCircle(center, radius, startAngle), false, false);
            context.ArcTo(
                PointOnCircle(center, radius, endAngle),
                new Size(radius, radius),
                0d,
                endAngle - startAngle > 180d,
                SweepDirection.Clockwise,
                true,
                false);
        }

        geometry.Freeze();

        var pen = new Pen(brush, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };

        pen.Freeze();
        drawingContext.DrawGeometry(null, pen, geometry);
    }

    private static Point PointOnCircle(Point center, double radius, double angle)
    {
        var radians = angle * Math.PI / 180d;
        return new Point(center.X + (radius * Math.Cos(radians)), center.Y + (radius * Math.Sin(radians)));
    }
}
