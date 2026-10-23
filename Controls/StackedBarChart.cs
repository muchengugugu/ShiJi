using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ShiJi.Services;

namespace ShiJi.Controls;

public sealed class ChartSegment
{
    public string Name { get; init; } = "";
    public string Color { get; init; } = "#6C5CE7";
    public double Value { get; init; }
}

public sealed class ChartBar
{
    public string Label { get; init; } = "";
    public string Tooltip { get; init; } = "";
    public List<ChartSegment> Segments { get; init; } = new();
    public double Total => Segments.Sum(s => s.Value);
}

public sealed class StackedBarChart : FrameworkElement
{
    private static readonly Typeface LabelTypeface = new(
        new FontFamily("Microsoft YaHei UI"),
        FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private readonly List<ChartBar> _bars = new();
    private readonly List<Rect> _barRects = new();
    private int _hoverIndex = -1;
    private double _maxValue = 1;

    public StackedBarChart()
    {
        SnapsToDevicePixels = true;
        ToolTipService.SetInitialShowDelay(this, 120);
        ToolTipService.SetBetweenShowDelay(this, 0);
        ToolTipService.SetShowDuration(this, 20000);
    }

    public void SetData(IEnumerable<ChartBar> bars)
    {
        _bars.Clear();
        _bars.AddRange(bars);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        _barRects.Clear();
        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 80 || height < 80) return;

        const double left = 4;
        const double right = 58;
        const double top = 12;
        const double bottom = 28;
        var plotW = width - left - right;
        var plotH = height - top - bottom;
        if (plotW <= 10 || plotH <= 10) return;

        var rawMax = _bars.Count == 0 ? 0 : _bars.Max(b => b.Total);
        var step = NiceStep(Math.Max(rawMax / 4.0, 30));
        _maxValue = Math.Max(step * 4, 60);

        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(233, 234, 241)), 1)
        {
            DashStyle = new DashStyle(new double[] { 2, 4 }, 0)
        };
        gridPen.Freeze();
        var axisBrush = new SolidColorBrush(Color.FromRgb(157, 159, 174));
        axisBrush.Freeze();
        var trackBrush = new SolidColorBrush(Color.FromRgb(247, 247, 251));
        trackBrush.Freeze();

        for (var i = 0; i <= 4; i++)
        {
            var y = top + plotH - plotH * i / 4.0;
            dc.DrawLine(gridPen, new Point(left, y), new Point(left + plotW, y));
            var label = FormatAxis(step * i);
            var ft = Text(label, 9.5, axisBrush);
            dc.DrawText(ft, new Point(left + plotW + 10, y - ft.Height / 2));
        }

        if (_bars.Count == 0 || rawMax <= 0)
        {
            var empty = Text("这个时间段还没有运行记录", 11.5, axisBrush);
            dc.DrawText(empty, new Point(left + (plotW - empty.Width) / 2, top + plotH / 2 - empty.Height / 2));
            return;
        }

        var slot = plotW / _bars.Count;
        var barW = Math.Min(30, Math.Max(5, slot * 0.58));
        var labelStep = _bars.Count > 26 ? 3 : _bars.Count > 16 ? 2 : 1;

        for (var i = 0; i < _bars.Count; i++)
        {
            var x = left + slot * i + (slot - barW) / 2;
            var rect = new Rect(x, top, barW, plotH);
            _barRects.Add(rect);

            if (_hoverIndex == i)
            {
                var hover = new SolidColorBrush(Color.FromArgb(26, 106, 90, 224));
                hover.Freeze();
                dc.DrawRoundedRectangle(hover, null, new Rect(x - 3, top - 4, barW + 6, plotH + 8), 6, 6);
            }

            dc.DrawRoundedRectangle(trackBrush, null, new Rect(x, top, barW, plotH), 4, 4);

            var bar = _bars[i];
            if (bar.Total > 0)
            {
                var barH = Math.Max(2, plotH * Math.Min(1, bar.Total / _maxValue));
                var yBottom = top + plotH;
                dc.PushClip(new RectangleGeometry(new Rect(x, yBottom - barH, barW, barH), 4, 4));
                foreach (var segment in bar.Segments.Where(s => s.Value > 0).OrderByDescending(s => s.Value))
                {
                    var h = barH * segment.Value / bar.Total;
                    yBottom -= h;
                    var brush = new SolidColorBrush(ColorUtil.Parse(segment.Color));
                    brush.Freeze();
                    dc.DrawRectangle(brush, null, new Rect(x, yBottom, barW, h + 0.5));
                }
                dc.Pop();
            }

            if (i % labelStep == 0 || i == _bars.Count - 1)
            {
                var label = Text(bar.Label, 9.5, axisBrush);
                var lx = x + barW / 2 - label.Width / 2;
                var ly = top + plotH + 9;
                if (lx < left) lx = left;
                if (lx + label.Width > left + plotW) lx = left + plotW - label.Width;
                dc.DrawText(label, new Point(lx, ly));
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = e.GetPosition(this);
        var index = -1;
        for (var i = 0; i < _barRects.Count; i++)
        {
            var rect = _barRects[i];
            if (p.X >= rect.X - 5 && p.X <= rect.Right + 5 && p.Y >= rect.Y && p.Y <= rect.Bottom + 20)
            {
                index = i;
                break;
            }
        }

        if (index != _hoverIndex)
        {
            _hoverIndex = index;
            ToolTip = index >= 0 ? _bars[index].Tooltip : null;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverIndex == -1) return;
        _hoverIndex = -1;
        ToolTip = null;
        InvalidateVisual();
    }

    private FormattedText Text(string text, double size, Brush brush) => new(
        text,
        CultureInfo.CurrentUICulture,
        FlowDirection.LeftToRight,
        LabelTypeface,
        size,
        brush,
        VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private static double NiceStep(double raw)
    {
        double[] steps =
        {
            10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600, 7200,
            10800, 21600, 43200, 86400, 172800, 604800
        };
        foreach (var step in steps)
            if (raw <= step) return step;
        return 2592000;
    }

    private static string FormatAxis(double seconds)
    {
        if (seconds <= 0) return "0";
        if (seconds < 60) return $"{seconds:0}秒";
        if (seconds < 3600)
        {
            var minutes = seconds / 60;
            return minutes % 1 == 0 ? $"{minutes:0}分钟" : $"{minutes:0.#}分钟";
        }
        var h = seconds / 3600;
        return h % 1 == 0 ? $"{h:0}小时" : $"{h:0.#}小时";
    }
}
