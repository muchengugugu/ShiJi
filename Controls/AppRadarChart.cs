using System.Globalization;
using System.Windows;
using System.Windows.Media;
using ShiJi.Services;

namespace ShiJi.Controls;

public sealed class AppRadarDatum
{
    public string Label { get; init; } = "";
    public double Value { get; init; }
}

/// <summary>Small radar chart for the top applications in the selected range.</summary>
public sealed class AppRadarChart : FrameworkElement
{
    private static readonly Typeface Typeface = new(
        new FontFamily("Microsoft YaHei UI"),
        FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private readonly List<AppRadarDatum> _data = new();

    public void SetData(IEnumerable<AppRadarDatum> data)
    {
        _data.Clear();
        _data.AddRange(data);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 100 || height < 100) return;

        var count = _data.Count;
        var cx = width / 2;
        var cy = height / 2 + 2;
        var radius = Math.Min(width, height) * 0.31;
        if (radius < 40) return;

        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(231, 232, 241)), 1);
        gridPen.Freeze();
        var axisPen = new Pen(new SolidColorBrush(Color.FromRgb(239, 240, 246)), 1);
        axisPen.Freeze();

        if (count == 0)
        {
            var empty = Text("这个时间段还没有应用记录", 11, ColorUtil.Brush("#A3A5B4"));
            dc.DrawText(empty, new Point(cx - empty.Width / 2, cy - empty.Height / 2));
            return;
        }

        var axes = Math.Max(3, count);
        for (var ring = 1; ring <= 4; ring++)
        {
            var r = radius * ring / 4.0;
            var points = new List<Point>();
            for (var i = 0; i < axes; i++)
                points.Add(PointOn(cx, cy, r, i, axes));
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(points[0], false, true);
                for (var i = 1; i < points.Count; i++) ctx.LineTo(points[i], true, false);
            }
            geometry.Freeze();
            dc.DrawGeometry(null, gridPen, geometry);
        }

        for (var i = 0; i < axes; i++)
            dc.DrawLine(axisPen, new Point(cx, cy), PointOn(cx, cy, radius, i, axes));

        var max = _data.Max(d => d.Value);
        if (max <= 0)
        {
            var empty = Text("这个时间段还没有应用记录", 11, ColorUtil.Brush("#A3A5B4"));
            dc.DrawText(empty, new Point(cx - empty.Width / 2, cy - empty.Height / 2));
        }
        else
        {
            var points = new List<Point>();
            for (var i = 0; i < axes; i++)
            {
                var value = i < _data.Count ? Math.Max(0, _data[i].Value) : 0;
                var ratio = Math.Clamp(value / max, 0, 1);
                points.Add(PointOn(cx, cy, radius * ratio, i, axes));
            }

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(points[0], true, true);
                for (var i = 1; i < points.Count; i++) ctx.LineTo(points[i], true, false);
            }
            geometry.Freeze();

            var fill = new SolidColorBrush(Color.FromArgb(56, 106, 90, 224));
            fill.Freeze();
            var outline = new Pen(new SolidColorBrush(Color.FromRgb(106, 90, 224)), 2);
            outline.Freeze();
            dc.DrawGeometry(fill, outline, geometry);

            for (var i = 0; i < count; i++)
            {
                var dot = new SolidColorBrush(Color.FromRgb(106, 90, 224));
                dot.Freeze();
                dc.DrawEllipse(dot, null, points[i], 2.5, 2.5);
            }
        }

        for (var i = 0; i < count; i++)
        {
            var label = TrimLabel(_data[i].Label);
            var value = _data[i].Value;
            var point = PointOn(cx, cy, radius + 27, i, axes);
            var text = Text(label, 10.5, ColorUtil.Brush("#787B8C"));
            var valueText = Text(value > 0 ? Fmt.Duration(value) : "—", 9.5, ColorUtil.Brush("#A3A5B4"));
            var x = point.X - text.Width / 2;
            var y = point.Y - (text.Height + valueText.Height) / 2;
            if (x < 2) x = 2;
            if (x + text.Width > width - 2) x = width - text.Width - 2;
            if (y < 2) y = 2;
            if (y + text.Height + valueText.Height > height - 2)
                y = height - text.Height - valueText.Height - 2;
            dc.DrawText(text, new Point(x, y));
            dc.DrawText(valueText, new Point(x + (text.Width - valueText.Width) / 2, y + text.Height + 1));
        }
    }

    private static string TrimLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) return "—";
        label = label.Trim();
        return label.Length <= 11 ? label : label[..10] + "…";
    }

    private static Point PointOn(double cx, double cy, double radius, int index, int count)
    {
        var angle = -Math.PI / 2 + index * 2 * Math.PI / count;
        return new Point(cx + radius * Math.Cos(angle), cy + radius * Math.Sin(angle));
    }

    private FormattedText Text(string text, double size, Brush brush) => new(
        text,
        CultureInfo.CurrentUICulture,
        FlowDirection.LeftToRight,
        Typeface,
        size,
        brush,
        VisualTreeHelper.GetDpi(this).PixelsPerDip);
}
