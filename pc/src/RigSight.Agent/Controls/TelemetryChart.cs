using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace RigSight.Agent.Controls;

/// <summary>
/// A lightweight live line chart drawn directly by WPF. Two series, automatic
/// scaling, dashed grid, and graceful handling of sensor gaps.
/// </summary>
public sealed class TelemetryChart : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<double?>), typeof(TelemetryChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SecondaryValuesProperty = DependencyProperty.Register(
        nameof(SecondaryValues), typeof(IReadOnlyList<double?>), typeof(TelemetryChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SeriesBrushProperty = DependencyProperty.Register(
        nameof(SeriesBrush), typeof(Brush), typeof(TelemetryChart),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0x6E, 0xF3, 0xC5)),
            FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SecondaryBrushProperty = DependencyProperty.Register(
        nameof(SecondaryBrush), typeof(Brush), typeof(TelemetryChart),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0xA7, 0x8B, 0xFA)),
            FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty EmptyTextProperty = DependencyProperty.Register(
        nameof(EmptyText), typeof(string), typeof(TelemetryChart),
        new FrameworkPropertyMetadata("Waiting for telemetry", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ValueSuffixProperty = DependencyProperty.Register(
        nameof(ValueSuffix), typeof(string), typeof(TelemetryChart),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double?>? Values
    {
        get => (IReadOnlyList<double?>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public IReadOnlyList<double?>? SecondaryValues
    {
        get => (IReadOnlyList<double?>?)GetValue(SecondaryValuesProperty);
        set => SetValue(SecondaryValuesProperty, value);
    }

    public Brush SeriesBrush
    {
        get => (Brush)GetValue(SeriesBrushProperty);
        set => SetValue(SeriesBrushProperty, value);
    }

    public Brush SecondaryBrush
    {
        get => (Brush)GetValue(SecondaryBrushProperty);
        set => SetValue(SecondaryBrushProperty, value);
    }

    public string EmptyText
    {
        get => (string)GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }

    public string ValueSuffix
    {
        get => (string)GetValue(ValueSuffixProperty);
        set => SetValue(ValueSuffixProperty, value);
    }

    public TelemetryChart()
    {
        SnapsToDevicePixels = true;
        ClipToBounds = true;
        MinHeight = 100;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 24 || height < 24) return;

        var dpi = VisualTreeHelper.GetDpi(this);
        var gridBrush = TryFindResource("BorderBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(0x23, 0x2D, 0x3D));
        var faintBrush = TryFindResource("TextFaintBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(0x5D, 0x6A, 0x7D));

        var left = 4.0;
        const double right = 38.0;
        const double top = 16.0;
        const double bottom = 18.0;
        var plotWidth = Math.Max(1, width - left - right);
        var plotHeight = Math.Max(1, height - top - bottom);
        var baseline = top + plotHeight;

        var values = Values;
        var hasData = values is not null && values.Any(v => v.HasValue);
        if (!hasData)
        {
            var text = MakeText(EmptyText, 12, faintBrush, dpi.PixelsPerDip);
            dc.DrawText(text, new Point((width - text.Width) / 2, (height - text.Height) / 2));
            return;
        }

        var all = new List<double>();
        all.AddRange(values!.Where(v => v.HasValue).Select(v => v!.Value));
        if (SecondaryValues is not null)
            all.AddRange(SecondaryValues.Where(v => v.HasValue).Select(v => v!.Value));

        var max = all.Count == 0 ? 1 : all.Max();
        if (max <= 0) max = 1;
        max = NiceCeiling(max);

        var gridPen = new Pen(WithOpacity(gridBrush, 0.75), 1) { DashStyle = new DashStyle([2, 4], 0) };
        gridPen.Freeze();
        var labelBrush = faintBrush;

        for (var row = 0; row <= 3; row++)
        {
            var y = top + plotHeight * row / 3.0;
            dc.DrawLine(gridPen, new Point(left, y), new Point(left + plotWidth, y));

            var labelValue = max * (3 - row) / 3.0;
            var label = MakeText(FormatAxis(labelValue) + (row == 0 ? ValueSuffix : ""), 9.5, labelBrush, dpi.PixelsPerDip);
            dc.DrawText(label, new Point(width - label.Width, y - label.Height / 2));
        }

        DrawSeries(dc, values!, max, left, top, plotWidth, plotHeight, SeriesBrush, 2, fill: true);
        if (SecondaryValues is not null && SecondaryValues.Any(v => v.HasValue))
        {
            var dashed = new Pen(WithOpacity(SecondaryBrush, 0.85), 1.5)
            {
                DashStyle = new DashStyle([4, 3], 0),
            };
            dashed.Freeze();
            DrawSeries(dc, SecondaryValues, max, left, top, plotWidth, plotHeight, SecondaryBrush, 1.5, fill: false, penOverride: dashed);
        }

        DrawLastDot(dc, values!, max, left, top, plotWidth, plotHeight, SeriesBrush);

        // Ground line.
        var groundPen = new Pen(WithOpacity(gridBrush, 0.9), 1);
        groundPen.Freeze();
        dc.DrawLine(groundPen, new Point(left, baseline), new Point(left + plotWidth, baseline));
    }

    private static Brush WithOpacity(Brush brush, double opacity)
    {
        var copy = brush.Clone();
        copy.Opacity = opacity;
        copy.Freeze();
        return copy;
    }

    private static void DrawSeries(
        DrawingContext dc,
        IReadOnlyList<double?> values,
        double max,
        double left,
        double top,
        double width,
        double height,
        Brush brush,
        double thickness,
        bool fill,
        Pen? penOverride = null)
    {
        var count = values.Count;
        if (count == 0) return;

        var line = new StreamGeometry();
        var area = new StreamGeometry();
        using (var lineCtx = line.Open())
        using (var areaCtx = area.Open())
        {
            var penStarted = false;
            var areaStarted = false;
            var lastX = 0.0;
            var baseline = top + height;

            for (var i = 0; i < count; i++)
            {
                var value = values[i];
                if (!value.HasValue)
                {
                    penStarted = false;
                    if (areaStarted)
                    {
                        areaCtx.LineTo(new Point(lastX, baseline), true, false);
                        areaCtx.Close();
                        areaStarted = false;
                    }
                    continue;
                }

                var x = count == 1 ? left : left + width * i / (count - 1.0);
                var y = baseline - Math.Clamp(value.Value / max, 0, 1) * height;

                if (!penStarted)
                {
                    lineCtx.BeginFigure(new Point(x, y), false, false);
                    penStarted = true;
                }
                else
                {
                    lineCtx.LineTo(new Point(x, y), true, false);
                }

                if (fill)
                {
                    if (!areaStarted)
                    {
                        areaCtx.BeginFigure(new Point(x, baseline), true, true);
                        areaCtx.LineTo(new Point(x, y), true, false);
                        areaStarted = true;
                    }
                    else
                    {
                        areaCtx.LineTo(new Point(x, y), true, false);
                    }
                }

                lastX = x;
            }

            if (areaStarted)
            {
                areaCtx.LineTo(new Point(lastX, baseline), true, false);
                areaCtx.Close();
            }
        }

        line.Freeze();
        if (fill)
        {
            area.Freeze();
            dc.PushOpacity(0.13);
            dc.DrawGeometry(brush, null, area);
            dc.Pop();
        }

        var pen = penOverride ?? new Pen(brush, thickness)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        pen.Freeze();
        dc.DrawGeometry(null, pen, line);
    }

    private static void DrawLastDot(
        DrawingContext dc, IReadOnlyList<double?> values, double max,
        double left, double top, double width, double height, Brush brush)
    {
        for (var i = values.Count - 1; i >= 0; i--)
        {
            if (!values[i].HasValue) continue;
            var x = values.Count == 1 ? left : left + width * i / (values.Count - 1.0);
            var y = top + height - Math.Clamp(values[i]!.Value / max, 0, 1) * height;
            dc.DrawEllipse(brush, null, new Point(x, y), 3.2, 3.2);
            return;
        }
    }

    private static double NiceCeiling(double value)
    {
        if (value <= 10) return Math.Ceiling(value);
        if (value <= 60) return Math.Ceiling(value / 10) * 10;
        if (value <= 300) return Math.Ceiling(value / 30) * 30;
        return Math.Ceiling(value / 100) * 100;
    }

    private static string FormatAxis(double value)
    {
        if (value >= 100) return value.ToString("0", CultureInfo.InvariantCulture);
        if (value >= 10) return value.ToString("0", CultureInfo.InvariantCulture);
        return value.ToString("0.#", CultureInfo.InvariantCulture);
    }

    private static FormattedText MakeText(string text, double size, Brush brush, double pixelsPerDip)
        => new(
            text ?? "",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI Variable Text"),
            size,
            brush,
            pixelsPerDip);
}
