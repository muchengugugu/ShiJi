using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ShiJi.Controls;
using ShiJi.Models;
using ShiJi.Services;

namespace ShiJi.Views;

public partial class OverviewView : UserControl, IRefreshablePage
{
    private string _period = "today";
    private bool _ready;
    private DateOnly _lastDay = TimeUtil.Today;
    private DateTime _lastRefresh = DateTime.MinValue;

    public OverviewView()
    {
        InitializeComponent();
        _ready = true;
        TodayText.Text = TimeUtil.Today.ToString("yyyy年M月d日") + " 周" + Fmt.Weekday(TimeUtil.Today);
        Loaded += (_, _) => Dispatcher.BeginInvoke(
            new Action(() => PageScroll.ScrollToTop()), DispatcherPriority.Loaded);
    }

    private void Period_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready || sender is not RadioButton { Tag: string tag }) return;
        _period = tag;
        RefreshData();
    }

    public void RefreshData()
    {
        if (!_ready) return;
        _lastRefresh = DateTime.Now;
        _lastDay = TimeUtil.Today;
        TodayText.Text = TimeUtil.Today.ToString("yyyy年M月d日") + " 周" + Fmt.Weekday(_lastDay);

        var now = DateTimeOffset.UtcNow;
        var apps = App.Store.GetApps();
        var sessions = App.Store.GetSessions();
        var runningIds = App.Tracker.GetRunningAppIds();

        HeaderStatusText.Text = runningIds.Count > 0 ? $"{runningIds.Count} 个运行中" : "监控中";
        OnboardingBanner.Visibility = apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LaunchAddButton.Visibility = apps.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        var range = GetRange(_lastDay);
        var summary = StatsService.Summarize(sessions, range.From, range.To, now, apps, runningIds.Count);
        TotalValue.Text = Fmt.Duration(summary.TotalSeconds);
        TotalSub.Text = $"{summary.AppCount} 个应用 · {summary.SessionCount} 段记录";
        LaunchValue.Text = $"{summary.LaunchCount} 次";
        LaunchSub.Text = summary.LaunchCount > 0 ? $"覆盖 {summary.AppCount} 个应用" : "还没有启动记录";
        LongestValue.Text = summary.LongestSeconds > 0 ? summary.LongestAppName : "—";
        LongestSub.Text = summary.LongestSeconds > 0 ? Fmt.Duration(summary.LongestSeconds) : "暂无记录";
        RunningValue.Text = runningIds.Count > 0 ? $"{runningIds.Count} 个" : "当前没有程序运行";
        RunningValue.FontSize = runningIds.Count > 0 ? 21 : 13;
        RunningSub.Text = runningIds.Count > 0 ? "此刻在后台运行" : "";
        PeriodRangeText.Text = range.Label;

        BuildChart(sessions, apps, runningIds, now, range);
        BuildRanking(sessions, apps, runningIds, now, range);
        BuildLaunchPanel(apps, sessions, runningIds, _lastDay, now);
    }

    private PeriodRange GetRange(DateOnly today)
    {
        var todayStart = TimeUtil.LocalDayStart(today);
        var todayEnd = TimeUtil.LocalDayEnd(today);
        if (_period == "week")
        {
            var weekStartDay = TimeUtil.WeekStart(today);
            var weekEndDay = weekStartDay.AddDays(6);
            return new PeriodRange
            {
                FromDay = weekStartDay,
                ToDay = weekEndDay,
                From = TimeUtil.LocalDayStart(weekStartDay),
                To = TimeUtil.LocalDayEnd(weekEndDay),
                Label = $"{weekStartDay.Month}月{weekStartDay.Day}日 – {weekEndDay.Month}月{weekEndDay.Day}日",
                Title = "本周每日运行分布",
                Subtitle = "周一至周日，按应用叠加",
                BucketCount = 7,
                BucketStart = i => TimeUtil.LocalDayStart(weekStartDay.AddDays(i)),
                BucketLabel = i => "周" + "一二三四五六日"[i],
                BucketTooltip = i => $"{weekStartDay.AddDays(i).Month}月{weekStartDay.AddDays(i).Day}日 周{"一二三四五六日"[i]}"
            };
        }

        if (_period == "month")
        {
            var monthStartDay = TimeUtil.MonthStart(today);
            var days = today.Day;
            return new PeriodRange
            {
                FromDay = monthStartDay,
                ToDay = today,
                From = TimeUtil.LocalDayStart(monthStartDay),
                To = todayEnd,
                Label = $"{today.Year}年{today.Month}月",
                Title = "本月每日运行分布",
                Subtitle = $"本月 1–{today.Day} 日，按应用叠加",
                BucketCount = days,
                BucketStart = i => TimeUtil.LocalDayStart(monthStartDay.AddDays(i)),
                BucketLabel = i => (i + 1).ToString(),
                BucketTooltip = i => $"{today.Month}月{i + 1}日 周{Fmt.Weekday(monthStartDay.AddDays(i))}"
            };
        }

        return new PeriodRange
        {
            FromDay = today,
            ToDay = today,
            From = todayStart,
            To = todayEnd,
            Label = TimeUtil.DayStartHour == 0
                ? "00:00 – 24:00"
                : $"{TimeUtil.DayStartHour:00}:00 – 次日 {TimeUtil.DayStartHour:00}:00",
            Title = "每小时运行分布",
            Subtitle = "按小时分布",
            BucketCount = 24,
            BucketStart = i => todayStart.AddHours(i),
            BucketLabel = i => todayStart.AddHours(i).Hour.ToString(),
            BucketTooltip = i =>
            {
                var begin = todayStart.AddHours(i);
                return $"{begin:HH:00} – {begin.AddHours(1):HH:00}";
            }
        };
    }

    private void BuildChart(
        IReadOnlyList<SessionRecord> sessions,
        IReadOnlyList<WatchedApp> apps,
        IReadOnlySet<string> runningIds,
        DateTimeOffset now,
        PeriodRange range)
    {
        ChartTitle.Text = range.Title;
        ChartSubtitle.Text = range.Subtitle;

        var aggregate = StatsService.AggregateApps(sessions, range.From, range.To, now, apps, runningIds);
        var top = aggregate.Where(a => a.Seconds > 0).Take(6).ToList();
        var topIds = top.Select(a => a.AppId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hasOther = aggregate.Any(a => a.Seconds > 0 && !topIds.Contains(a.AppId));

        var buckets = StatsService.BuildBuckets(
            sessions, range.From, range.To, range.BucketCount, range.BucketStart, now, apps);

        var bars = new List<ChartBar>();
        for (var i = 0; i < buckets.Count; i++)
        {
            var bucket = buckets[i];
            var segments = new List<ChartSegment>();
            foreach (var slice in bucket.Slices)
            {
                if (!topIds.Contains(slice.AppId)) continue;
                segments.Add(new ChartSegment { Name = slice.Name, Color = slice.Color, Value = slice.Seconds });
            }
            var otherSeconds = bucket.Slices.Where(s => !topIds.Contains(s.AppId)).Sum(s => s.Seconds);
            if (otherSeconds > 0)
                segments.Add(new ChartSegment { Name = "其他", Color = "#C8CAD8", Value = otherSeconds });

            var parts = new List<string> { range.BucketTooltip(i) };
            foreach (var slice in bucket.Slices.OrderByDescending(s => s.Seconds).Take(6))
                parts.Add($"{slice.Name}  {Fmt.Duration(slice.Seconds)}");
            parts.Add($"合计  {Fmt.Duration(bucket.Total)}");

            bars.Add(new ChartBar
            {
                Label = range.BucketLabel(i),
                Tooltip = string.Join("\n", parts),
                Segments = segments
            });
        }

        Chart.SetData(bars);
        ChartTotalText.Text = "合计 " + Fmt.Duration(aggregate.Sum(a => a.Seconds));

        ChartLegend.Children.Clear();
        foreach (var item in top)
            ChartLegend.Children.Add(LegendItem(item.Color, item.Name));
        if (hasOther) ChartLegend.Children.Add(LegendItem("#C8CAD8", "其他"));

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

    private void BuildRanking(
        IReadOnlyList<SessionRecord> sessions,
        IReadOnlyList<WatchedApp> apps,
        IReadOnlySet<string> runningIds,
        DateTimeOffset now,
        PeriodRange range)
    {
        var aggregate = StatsService.AggregateApps(sessions, range.From, range.To, now, apps, runningIds)
            .Where(a => a.Seconds > 0)
            .Take(8)
            .ToList();

        RankingPanel.Children.Clear();
        RankingEmpty.Visibility = aggregate.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RankingPanel.Visibility = aggregate.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        RankingCountText.Text = aggregate.Count == 0 ? "" : $"Top {aggregate.Count}";

        var max = aggregate.FirstOrDefault()?.Seconds ?? 0;
        var index = 1;
        foreach (var app in aggregate)
        {
            var row = new Grid { Margin = new Thickness(0, 7, 0, 7) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(158) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });

            var badge = Ui.AppBadge(app.Name, app.Color, apps.FirstOrDefault(a => a.Id == app.AppId)?.ExecutablePath, 30);
            Grid.SetColumn(badge, 0);
            row.Children.Add(badge);

            var rank = new TextBlock
            {
                Text = index.ToString(),
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = ColorUtil.Brush(index <= 3 ? "#6A5AE0" : "#B3B5C4"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(rank, 1);
            row.Children.Add(rank);

            var namePanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var name = new TextBlock
            {
                Text = app.Name,
                FontSize = 12.5,
                FontWeight = FontWeights.Medium,
                Foreground = ColorUtil.Brush("#1D1E2A"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 150
            };
            namePanel.Children.Add(name);
            var sub = new TextBlock
            {
                Text = $"{app.Launches} 次启动",
                FontSize = 9.5,
                Foreground = ColorUtil.Brush("#9A9CAB"),
                Margin = new Thickness(0, 2, 0, 0)
            };
            namePanel.Children.Add(sub);
            Grid.SetColumn(namePanel, 2);
            row.Children.Add(namePanel);

            var bar = new ProgressBar
            {
                Style = (Style)System.Windows.Application.Current.FindResource("RankBar"),
                Minimum = 0,
                Maximum = Math.Max(1, max),
                Value = app.Seconds,
                Foreground = ColorUtil.Brush(app.Color),
                Height = 6,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 12, 0)
            };
            Grid.SetColumn(bar, 3);
            row.Children.Add(bar);

            var duration = new TextBlock
            {
                Text = Fmt.Duration(app.Seconds),
                FontSize = 11,
                Foreground = ColorUtil.Brush("#6F7182"),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(duration, 4);
            row.Children.Add(duration);

            RankingPanel.Children.Add(row);
            if (index < aggregate.Count)
                RankingPanel.Children.Add(new Border { Height = 1, Background = ColorUtil.Brush("#F4F4F8") });
            index++;
        }
    }

    private void BuildLaunchPanel(
        IReadOnlyList<WatchedApp> apps,
        IReadOnlyList<SessionRecord> sessions,
        IReadOnlySet<string> runningIds,
        DateOnly day,
        DateTimeOffset now)
    {
        LaunchPanel.Children.Clear();
        var displayApps = apps.Where(a => a.ShowInToday).ToList();
        var status = StatsService.GetDayStatus(displayApps, sessions, day, now, runningIds)
            .OrderByDescending(s => s.Running)
            .ThenByDescending(s => s.Launched)
            .ThenByDescending(s => s.TodaySeconds)
            .ToList();

        var launched = status.Count(s => s.Launched);
        LaunchRatioText.Text = $"{launched} / {displayApps.Count}";
        LaunchEmpty.Visibility = displayApps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LaunchScroll.Visibility = displayApps.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        var noApps = apps.Count == 0;
        LaunchEmptyTitle.Text = noApps ? "名单还是空的" : "没有显示的应用";
        LaunchEmptySub.Text = noApps
            ? "添加程序后，这里会显示今天是否启动过。"
            : "在应用名单里打开「显示」后，这里会显示今日启动。";
        LaunchEmptyAddButton.Visibility = noApps ? Visibility.Visible : Visibility.Collapsed;

        for (var i = 0; i < status.Count; i++)
        {
            var item = status[i];
            var row = new Grid { Margin = new Thickness(0, 8, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var badge = Ui.AppBadge(item.App, 36);
            badge.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(badge, 0);
            row.Children.Add(badge);

            var textPanel = new StackPanel { Margin = new Thickness(2, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            var name = new TextBlock
            {
                Text = item.App.Name,
                FontSize = 12.5,
                FontWeight = FontWeights.Medium,
                Foreground = ColorUtil.Brush(item.App.Enabled ? "#1D1E2A" : "#9A9CAB"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 190
            };
            textPanel.Children.Add(name);

            string subText;
            if (item.Launched && item.FirstLaunch is not null)
                subText = $"首次 {Fmt.Clock(item.FirstLaunch)} · 今日 {Fmt.Duration(item.TodaySeconds)}";
            else if (item.Running)
                subText = $"跨天运行 · 今日 {Fmt.Duration(item.TodaySeconds)}";
            else
                subText = "今天还没有启动";

            textPanel.Children.Add(new TextBlock
            {
                Text = subText,
                FontSize = 10,
                Foreground = ColorUtil.Brush("#9A9CAB"),
                Margin = new Thickness(0, 3, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            Grid.SetColumn(textPanel, 1);
            row.Children.Add(textPanel);

            var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            var pill = Ui.StatusPill(item.Launched, item.Running);
            pill.HorizontalAlignment = HorizontalAlignment.Right;
            right.Children.Add(pill);
            if (item.Launches > 1)
            {
                right.Children.Add(new TextBlock
                {
                    Text = $"{item.Launches} 次",
                    FontSize = 9.5,
                    Foreground = ColorUtil.Brush("#9A9CAB"),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 3, 1, 0)
                });
            }
            Grid.SetColumn(right, 2);
            row.Children.Add(right);

            LaunchPanel.Children.Add(row);
            if (i < status.Count - 1)
                LaunchPanel.Children.Add(new Border { Height = 1, Background = ColorUtil.Brush("#F4F4F8") });
        }
    }

    private FrameworkElement LegendItem(string color, string name)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 17, 5)
        };
        panel.Children.Add(new Ellipse
        {
            Width = 7,
            Height = 7,
            Fill = ColorUtil.Brush(color),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        });
        panel.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 10.5,
            Foreground = ColorUtil.Brush("#747688"),
            MaxWidth = 120,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        return panel;
    }

    private void AddFirst_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is not MainWindow main) return;
        main.OpenWatchlist().BeginAdd();
    }

    public void OnTick()
    {
        if (!_ready) return;
        if (TimeUtil.Today != _lastDay)
        {
            RefreshData();
            return;
        }

        var running = App.Tracker.RunningAppCount;
        if (running <= 0) return;
        if (Chart.IsMouseOver || LaunchScroll.IsMouseOver) return;
        if ((DateTime.Now - _lastRefresh).TotalSeconds < 25) return;
        RefreshData();
    }

    private sealed class PeriodRange
    {
        public DateOnly FromDay { get; init; }
        public DateOnly ToDay { get; init; }
        public DateTimeOffset From { get; init; }
        public DateTimeOffset To { get; init; }
        public string Label { get; init; } = "";
        public string Title { get; init; } = "";
        public string Subtitle { get; init; } = "";
        public int BucketCount { get; init; }
        public Func<int, DateTimeOffset> BucketStart { get; init; } = _ => DateTimeOffset.UtcNow;
        public Func<int, string> BucketLabel { get; init; } = _ => "";
        public Func<int, string> BucketTooltip { get; init; } = _ => "";
    }
}
