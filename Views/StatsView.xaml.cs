using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ShiJi.Controls;
using ShiJi.Models;
using ShiJi.Services;

namespace ShiJi.Views;

public partial class StatsView : UserControl, IRefreshablePage
{
    private DateOnly _anchor = TimeUtil.Today;
    private bool _ready;
    private DateTime _lastRefresh = DateTime.MinValue;

    public StatsView()
    {
        InitializeComponent();
        _ready = true;
        Loaded += (_, _) => Dispatcher.BeginInvoke(
            new Action(() => PageScroll.ScrollToTop()),
            System.Windows.Threading.DispatcherPriority.Loaded);
        RefreshData();
    }

    private void Period_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready || sender is not RadioButton { Tag: string }) return;
        _anchor = TimeUtil.Today;
        RefreshData();
    }

    private void Previous_Click(object sender, RoutedEventArgs e)
    {
        _anchor = Periods.Shift(CurrentPeriod, _anchor, -1);
        RefreshData();
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        _anchor = Periods.Shift(CurrentPeriod, _anchor, 1);
        RefreshData();
    }

    private void Today_Click(object sender, RoutedEventArgs e)
    {
        _anchor = TimeUtil.Today;
        RefreshData();
    }

    private StatsPeriod CurrentPeriod =>
        PeriodWeek.IsChecked == true ? StatsPeriod.Week
        : PeriodMonth.IsChecked == true ? StatsPeriod.Month
        : PeriodYear.IsChecked == true ? StatsPeriod.Year
        : StatsPeriod.Day;

    public void RefreshData()
    {
        if (!_ready) return;
        _lastRefresh = DateTime.Now;
        _anchor = ClampAnchor(_anchor);
        var now = DateTimeOffset.UtcNow;
        var range = Periods.Build(CurrentPeriod, _anchor);
        var apps = App.Store.GetApps();
        var sessions = App.Store.GetSessions();
        var appMap = apps.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);
        var runningIds = App.Tracker.GetRunningAppIds();

        PeriodRangeText.Text = range.Label;
        FilterSummaryText.Text = "全部应用 · " + (CurrentPeriod switch
        {
            StatsPeriod.Week => "按周",
            StatsPeriod.Month => "按月",
            StatsPeriod.Year => "按年",
            _ => "按天"
        });

        var summary = StatsService.Summarize(sessions, range.From, range.To, now, apps, runningIds.Count);
        var aggregate = StatsService.AggregateApps(sessions, range.From, range.To, now, apps)
            .Where(a => a.Seconds > 0)
            .ToList();

        TotalValue.Text = Fmt.Duration(summary.TotalSeconds);
        TotalSub.Text = $"{summary.SessionCount} 段记录 · 共 {summary.AppCount} 个应用";
        AppCountValue.Text = summary.AppCount.ToString();
        AppCountSub.Text = summary.AppCount == 0 ? "这个时间段还没有应用" : "有运行记录的应用";
        LongestValue.Text = summary.LongestSeconds > 0 ? summary.LongestAppName : "—";
        LongestSub.Text = summary.LongestSeconds > 0 ? Fmt.Duration(summary.LongestSeconds) : "暂无记录";
        ChartTitle.Text = range.Title;
        ChartSubtitle.Text = range.Subtitle;
        ChartTotalText.Text = "合计 " + Fmt.Duration(summary.TotalSeconds);

        BuildChart(sessions, appMap, range, now, aggregate);
        Radar.SetData(aggregate.Take(5).Select(a => new AppRadarDatum
        {
            Label = a.Name,
            Value = a.Seconds
        }));
        BuildMostUsed(aggregate, apps);
    }

    private void BuildChart(
        IReadOnlyList<SessionRecord> sessions,
        IReadOnlyDictionary<string, WatchedApp> appMap,
        PeriodWindow range,
        DateTimeOffset now,
        IReadOnlyList<AppAggregate> aggregate)
    {
        var top = aggregate.Take(6).ToList();
        var topIds = top.Select(a => a.AppId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hasOther = aggregate.Any(a => !topIds.Contains(a.AppId));

        var buckets = StatsService.BuildBuckets(
            sessions, range.From, range.To, range.BucketCount, range.BucketStart, now,
            appMap.Values.ToList());

        var bars = new List<ChartBar>();
        for (var i = 0; i < buckets.Count; i++)
        {
            var bucket = buckets[i];
            var segments = bucket.Slices
                .Where(s => topIds.Contains(s.AppId))
                .Select(s => new ChartSegment { Name = s.Name, Color = s.Color, Value = s.Seconds })
                .ToList();
            var otherSeconds = bucket.Slices.Where(s => !topIds.Contains(s.AppId)).Sum(s => s.Seconds);
            if (otherSeconds > 0)
                segments.Add(new ChartSegment { Name = "其他", Color = "#C8CAD8", Value = otherSeconds });

            var tooltip = new List<string> { range.BucketTooltip(i) };
            foreach (var slice in bucket.Slices.OrderByDescending(s => s.Seconds).Take(6))
                tooltip.Add($"{slice.Name}  {Fmt.Duration(slice.Seconds)}");
            tooltip.Add($"合计  {Fmt.Duration(bucket.Total)}");

            bars.Add(new ChartBar
            {
                Label = range.BucketLabel(i),
                Tooltip = string.Join("\n", tooltip),
                Segments = segments
            });
        }

        Chart.SetData(bars);
        ChartLegend.Children.Clear();
        foreach (var item in top)
            ChartLegend.Children.Add(AppLegend(item.Color, item.Name));
        if (hasOther) ChartLegend.Children.Add(AppLegend("#C8CAD8", "其他"));
        if (top.Count == 0)
        {
            ChartLegend.Children.Add(new TextBlock
            {
                Text = "还没有运行记录",
                FontSize = 10.5,
                Foreground = ColorUtil.Brush("#A3A5B4"),
                Margin = new Thickness(2, 7, 0, 0)
            });
        }
    }

    private void BuildMostUsed(IReadOnlyList<AppAggregate> aggregate, IReadOnlyList<WatchedApp> apps)
    {
        var top = aggregate.Take(5).ToList();
        MostUsedPanel.Children.Clear();
        MostUsedEmpty.Visibility = top.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        MostUsedCountText.Text = top.Count == 0 ? "" : $"Top {top.Count}";
        if (top.Count == 0) return;

        var max = top[0].Seconds;
        foreach (var item in top)
            MostUsedPanel.Children.Add(MostUsedRow(item, apps, max));
    }

    private UIElement MostUsedRow(AppAggregate item, IReadOnlyList<WatchedApp> apps, double max)
    {
        var app = apps.FirstOrDefault(a => a.Id == item.AppId);
        var row = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });

        var badge = Ui.AppBadge(item.Name, item.Color, app?.ExecutablePath, 28);
        row.Children.Add(badge);

        var info = new StackPanel { Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock
        {
            Text = item.Name,
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = ColorUtil.Brush("#2C2D3A"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = item.Name
        });

        var track = new Grid { Height = 5, Margin = new Thickness(0, 5, 0, 0) };
        track.Children.Add(new Border
        {
            Background = ColorUtil.Brush("#F0F1F6"),
            CornerRadius = new CornerRadius(3)
        });
        track.Children.Add(new Border
        {
            Background = ColorUtil.Brush(item.Color),
            CornerRadius = new CornerRadius(3),
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = max <= 0 ? 0 : 145 * Math.Clamp(item.Seconds / max, 0.03, 1)
        });
        info.Children.Add(track);
        Grid.SetColumn(info, 1);
        row.Children.Add(info);

        var duration = new TextBlock
        {
            Text = Fmt.Duration(item.Seconds),
            FontSize = 10.5,
            Foreground = ColorUtil.Brush("#6B6E80"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(duration, 2);
        row.Children.Add(duration);
        return row;
    }

    private UIElement AppLegend(string color, string name)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(2, 4, 14, 2)
        };
        panel.Children.Add(new Ellipse
        {
            Width = 7,
            Height = 7,
            Fill = ColorUtil.Brush(color),
            VerticalAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 10.5,
            Foreground = ColorUtil.Brush("#777A8A"),
            Margin = new Thickness(5, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        return panel;
    }

    private static DateOnly ClampAnchor(DateOnly anchor) =>
        anchor > TimeUtil.Today ? TimeUtil.Today : anchor;

    public void OnTick()
    {
        if (App.Tracker.RunningAppCount <= 0) return;
        if ((DateTime.Now - _lastRefresh).TotalSeconds < 10) return;
        RefreshData();
    }
}
