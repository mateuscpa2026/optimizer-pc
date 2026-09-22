using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;

namespace OptimizerPC.App.Controls;

/// <summary>
/// Grafico de linha continuo para o monitor em tempo real. Mantem um historico
/// circular e redesenha a cada amostra recebida.
/// </summary>
public sealed class Sparkline : FrameworkElement
{
    private readonly List<double> _values = new();

    public static readonly DependencyProperty CapacityProperty = DependencyProperty.Register(
        nameof(Capacity),
        typeof(int),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(60, FrameworkPropertyMetadataOptions.AffectsRender, OnCapacityChanged));

    public static readonly DependencyProperty MaxValueProperty = DependencyProperty.Register(
        nameof(MaxValue),
        typeof(double),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(100d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AutoScaleProperty = DependencyProperty.Register(
        nameof(AutoScale),
        typeof(bool),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke),
        typeof(Brush),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(
            new SolidColorBrush(Color.FromRgb(0x2A, 0xC7, 0xEA)),
            FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness),
        typeof(double),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(1.6d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill),
        typeof(Brush),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values),
        typeof(IEnumerable<double>),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(null, OnValuesChanged));

    public int Capacity
    {
        get => (int)GetValue(CapacityProperty);
        set => SetValue(CapacityProperty, value);
    }

    public double MaxValue
    {
        get => (double)GetValue(MaxValueProperty);
        set => SetValue(MaxValueProperty, value);
    }

    /// <summary>Quando verdadeiro, a escala acompanha o maior valor observado.</summary>
    public bool AutoScale
    {
        get => (bool)GetValue(AutoScaleProperty);
        set => SetValue(AutoScaleProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>Historico observavel ligado ao controle; dispensa chamadas manuais a Push.</summary>
    public IEnumerable<double>? Values
    {
        get => (IEnumerable<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public int SampleCount => _values.Count;

    public void Push(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return;
        }

        var capacity = Math.Max(2, Capacity);
        if (_values.Count >= capacity)
        {
            _values.RemoveAt(0);
        }

        _values.Add(value);
        InvalidateVisual();
    }

    public void Reset()
    {
        _values.Clear();
        InvalidateVisual();
    }

    private static void OnCapacityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var spark = (Sparkline)d;
        var capacity = Math.Max(2, (int)e.NewValue);
        while (spark._values.Count > capacity)
        {
            spark._values.RemoveAt(0);
        }
    }

    private static void OnValuesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var spark = (Sparkline)d;

        if (e.OldValue is INotifyCollectionChanged previous)
        {
            previous.CollectionChanged -= spark.OnSourceCollectionChanged;
        }

        if (e.NewValue is INotifyCollectionChanged current)
        {
            current.CollectionChanged += spark.OnSourceCollectionChanged;
        }

        spark.RebuildFromSource();
    }

    private void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildFromSource();

    private void RebuildFromSource()
    {
        _values.Clear();

        if (Values is null)
        {
            InvalidateVisual();
            return;
        }

        var capacity = Math.Max(2, Capacity);
        foreach (var value in Values)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                continue;
            }

            if (_values.Count >= capacity)
            {
                _values.RemoveAt(0);
            }

            _values.Add(value);
        }

        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 2 || height <= 2 || _values.Count < 2)
        {
            return;
        }

        var pen = new Pen(Stroke, Math.Max(1d, StrokeThickness))
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };

        pen.Freeze();

        var pad = pen.Thickness;
        var top = pad;
        var bottom = height - pad;
        var usableHeight = Math.Max(1d, bottom - top);
        var steps = Math.Max(1, Math.Max(2, Capacity) - 1);
        var stepX = (width - (pad * 2d)) / steps;

        var scale = AutoScale
            ? Math.Max(1e-6, _values.Max())
            : Math.Max(1e-6, MaxValue);

        var points = new Point[_values.Count];
        for (var i = 0; i < _values.Count; i++)
        {
            var ratio = Math.Clamp(_values[i] / scale, 0d, 1d);
            points[i] = new Point(pad + (i * stepX), bottom - (ratio * usableHeight));
        }

        if (Fill is not null)
        {
            var area = new StreamGeometry();
            using (var context = area.Open())
            {
                context.BeginFigure(new Point(points[0].X, bottom), true, true);
                context.PolyLineTo(points, true, false);
                context.LineTo(new Point(points[^1].X, bottom), true, false);
            }

            area.Freeze();
            drawingContext.DrawGeometry(Fill, null, area);
        }

        var line = new StreamGeometry();
        using (var context = line.Open())
        {
            context.BeginFigure(points[0], false, false);
            context.PolyLineTo(points, true, false);
        }

        line.Freeze();
        drawingContext.DrawGeometry(null, pen, line);

        var last = points[^1];
        drawingContext.DrawEllipse(Stroke, null, last, pen.Thickness + 0.6d, pen.Thickness + 0.6d);
    }
}
