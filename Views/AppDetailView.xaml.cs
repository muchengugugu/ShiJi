using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ShiJi.Controls;
using ShiJi.Models;
using ShiJi.Services;

namespace ShiJi.Views;

public partial class AppDetailView : UserControl, IRefreshablePage
{
    private string _appId = "";
    private StatsPeriod _period = StatsPeriod.Day;
    private DateOnly _anchor = TimeUtil.Today;
    private DateOnly _monthAnchor = TimeUtil.MonthStart(TimeUtil.Today);
    private bool _ready;
    private bool _loading;
    private readonly HashSet<string> _expandedLaunchGroups = new();

    public AppDetailView()
    {
        InitializeComponent();
        _ready = true;
        Loaded += (_, _) => Dispatcher.BeginInvoke(
            new Action(() => PageScroll.ScrollToTop()),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    public event Action? BackRequested;

    public void ResetLaunchExpansion() => _expandedLaunchGroups.Clear();

    public void LoadApp(string appId, StatsPeriod period, DateOnly anchor)
    {
        _appId = appId;
        _period = period;
        _anchor = anchor > TimeUtil.Today ? TimeUtil.Today : anchor;
        _monthAnchor = TimeUtil.MonthStart(_anchor == default ? TimeUtil.Today : _anchor);
        _expandedLaunchGroups.Clear();
        _loading = true;
        PeriodDay.IsChecked = period == StatsPeriod.Day;
        PeriodWeek.IsChecked = period == StatsPeriod.Week;
        PeriodMonth.IsChecked = period == StatsPeriod.Month;
        PeriodYear.IsChecked = period == StatsPeriod.Year;
        _loading = false;
        PageScroll.ScrollToTop();
        RefreshData();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke();

    private void Period_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading || !_ready || sender is not RadioButton { Tag: string tag }) return;
        _period = Periods.Parse(tag);
        _anchor = TimeUtil.Today;
        _expandedLaunchGroups.Clear();
        RefreshData();
    }

    private void Previous_Click(object sender, RoutedEventArgs e)
    {
        _anchor = Periods.Shift(_period, _anchor, -1);
        _expandedLaunchGroups.Clear();
        RefreshData();
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        var next = Periods.Shift(_period, _anchor, 1);
        _anchor = next > TimeUtil.Today ? TimeUtil.Today : next;
        _expandedLaunchGroups.Clear();
        RefreshData();
    }

    private void Today_Click(object sender, RoutedEventArgs e)
    {
        _anchor = TimeUtil.Today;
        _monthAnchor = TimeUtil.MonthStart(_anchor);
        _expandedLaunchGroups.Clear();
        RefreshData();
    }

    private void RenameApp_Click(object sender, RoutedEventArgs e)
    {
        var app = App.Store.GetApp(_appId);
        if (app is null) return;
        try
        {
            var dialog = new EditAppDialog(app) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() != true) return;
            App.Store.UpdateApp(_appId, a =>
            {
                a.Name = dialog.AppName;
                a.Color = dialog.AppColor;
            });
            RefreshData();
            ShowToast("应用名称已更新。", ToastKind.Success);
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowToast("重命名失败：" + ex.Message, ToastKind.Error);
        }
    }

    private void MonthPrevious_Click(object sender, RoutedEventArgs e)    {
        _monthAnchor = _monthAnchor.AddMonths(-1);
        if (_period == StatsPeriod.Month) _anchor = _monthAnchor;
        RefreshData();
    }

    private void MonthNext_Click(object sender, RoutedEventArgs e)
    {
        var next = _monthAnchor.AddMonths(1);
        if (next <= TimeUtil.MonthStart(TimeUtil.Today)) _monthAnchor = next;
        if (_period == StatsPeriod.Month) _anchor = _monthAnchor;
        RefreshData();
    }

    public void RefreshData()
    {
        if (!_ready || _appId.Length == 0) return;
        if (_anchor > TimeUtil.Today) _anchor = TimeUtil.Today;
        var allSessions = App.Store.GetSessions();
        var appSessions = allSessions.Where(s => s.AppId == _appId).ToList();
        if (_period == StatsPeriod.Month)
            _monthAnchor = TimeUtil.MonthStart(_anchor);
        var app = App.Store.GetApp(_appId);
        var isRemoved = app is null;
        if (isRemoved)
        {
            if (appSessions.Count == 0)
            {
                BackRequested?.Invoke();
                return;
            }
            app = new WatchedApp
            {
                Id = _appId,
                Name = appSessions[0].AppName,
                Color = appSessions[0].AppColor,
                ExecutablePath = ""
            };
        }

        var resolved = app!;
        AppBadgeHost.Content = Ui.AppBadge(resolved, 58);
        AppNameText.Text = resolved.Name;
        AppPathText.Text = isRemoved ? "应用已移出名单" : resolved.ExecutablePath;
        RenameAppButton.IsEnabled = !isRemoved;

        var now = DateTimeOffset.UtcNow;
        var range = Periods.Build(_period, _anchor);
        var summary = StatsService.Summarize(appSessions, range.From, range.To, now);
        PeriodRangeText.Text = range.Label;
        TotalValue.Text = Fmt.Duration(summary.TotalSeconds);
        ChartTitle.Text = range.Title;
        ChartSubtitle.Text = range.Subtitle;
        EmptyChartText.Visibility = summary.TotalSeconds <= 0 ? Visibility.Visible : Visibility.Collapsed;

        BuildChart(appSessions, resolved, range, now);
        BuildMonth(resolved, appSessions, allSessions, now);
        BuildLaunchList(appSessions, range, now);
    }

    private void BuildLaunchList(
        IReadOnlyList<SessionRecord> sessions,
        PeriodWindow range,
        DateTimeOffset now)
    {
        LaunchGroupsPanel.Children.Clear();

        var inRange = sessions
            .Where(s =>
            {
                var logicalDay = TimeUtil.LocalDate(s.Start);
                return logicalDay >= range.FromDay && logicalDay <= range.ToDay;
            })
            .OrderBy(s => s.Start)
            .ToList();

        var byDay = inRange
            .GroupBy(s => TimeUtil.LocalDate(s.Start))
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.Start).ToList());

        LaunchSummaryText.Text = $"共 {inRange.Count} 次";
        LaunchHintText.Text = _period switch
        {
            StatsPeriod.Day => "按天：展开当天，查看每一次启动",
            StatsPeriod.Week => "按周：只显示有记录的日期",
            StatsPeriod.Month => "按月：只显示有记录的周",
            _ => "按年：只显示有记录的月"
        };

        var roots = new List<LaunchNode>();
        switch (_period)
        {
            case StatsPeriod.Year:
            {
                for (var month = 1; month <= 12; month++)
                    roots.Add(BuildMonthNode(range.FromDay.Year, month, byDay));
                break;
            }
            case StatsPeriod.Month:
            {
                var days = new List<DateOnly>();
                for (var day = range.FromDay; day <= range.ToDay; day = day.AddDays(1))
                    days.Add(day);
                foreach (var week in days.GroupBy(TimeUtil.WeekStart).OrderBy(g => g.Key))
                {
                    roots.Add(BuildWeekNode(
                        week.Key, week.ToList(), byDay, $"m:{range.FromDay:yyyyMM}"));
                }
                break;
            }
            case StatsPeriod.Week:
            {
                for (var i = 0; i < 7; i++)
                    roots.Add(BuildDayNode(range.FromDay.AddDays(i), byDay));
                break;
            }
            default:
                roots.Add(BuildDayNode(range.FromDay, byDay));
                break;
        }

        roots = roots.Where(node => node.Count() > 0).ToList();
        LaunchEmptyText.Visibility = roots.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        foreach (var root in roots)
        {
            LaunchGroupsPanel.Children.Add(BuildLaunchNode(
                root, now, forceExpanded: false));
        }
    }

    private UIElement BuildLaunchNode(LaunchNode node, DateTimeOffset now, bool forceExpanded)
    {
        var root = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
        var expanded = forceExpanded || _expandedLaunchGroups.Contains(node.Key);
        var count = node.Count();
        var seconds = node.Seconds(now);

        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var chevron = new TextBlock
        {
            Text = "›",
            FontSize = 15,
            Foreground = ColorUtil.Brush("#8B8D9E"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform(expanded ? 90 : 0)
        };
        Grid.SetColumn(chevron, 0);
        headerGrid.Children.Add(chevron);

        var title = new TextBlock
        {
            Text = node.Title,
            FontSize = node.Level == 0 ? 11.5 : node.Level == 1 ? 11 : 10.5,
            FontWeight = node.Level == 0 ? FontWeights.SemiBold : FontWeights.Medium,
            Foreground = ColorUtil.Brush(node.Level == 0 ? "#303140" : "#4A4C5E"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 10, 0)
        };
        Grid.SetColumn(title, 1);
        headerGrid.Children.Add(title);

        var meta = new TextBlock
        {
            Text = count == 0 ? "0 次" : $"{count} 次 · {Fmt.Duration(seconds)}",
            FontSize = 10.5,
            Foreground = ColorUtil.Brush("#8B8D9E"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(meta, 2);
        headerGrid.Children.Add(meta);

        var header = new Border
        {
            Background = ColorUtil.Brush(node.Level == 0 ? "#FAFAFD" : "#FCFCFE"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(9, 7, 9, 7),
            Child = headerGrid,
            Cursor = count > 0 ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow
        };

        var body = new StackPanel
        {
            Margin = new Thickness(14, 2, 0, 2),
            Visibility = expanded && count > 0 ? Visibility.Visible : Visibility.Collapsed
        };

        foreach (var child in node.Children)
            body.Children.Add(BuildLaunchNode(child, now, false));
        foreach (var session in node.Sessions)
            body.Children.Add(BuildLaunchRow(session, now));

        if (count > 0)
        {
            header.MouseLeftButtonUp += (_, _) =>
            {
                var show = body.Visibility != Visibility.Visible;
                body.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                chevron.RenderTransform = new RotateTransform(show ? 90 : 0);
                if (show) _expandedLaunchGroups.Add(node.Key);
                else _expandedLaunchGroups.Remove(node.Key);
            };
        }

        root.Children.Add(header);
        root.Children.Add(body);
        return root;
    }

    private LaunchNode BuildMonthNode(
        int year, int month, IReadOnlyDictionary<DateOnly, List<SessionRecord>> byDay)
    {
        var node = new LaunchNode
        {
            Key = $"m:{year}{month:00}",
            Title = $"{year}年{month}月",
            Level = 0
        };
        var days = new List<DateOnly>();
        var last = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        for (var day = new DateOnly(year, month, 1); day <= last; day = day.AddDays(1))
            days.Add(day);

        foreach (var week in days.GroupBy(TimeUtil.WeekStart).OrderBy(g => g.Key))
        {
            node.Children.Add(BuildWeekNode(
                week.Key, week.ToList(), byDay, $"m:{year}{month:00}"));
        }
        node.Children.RemoveAll(child => child.Count() == 0);
        return node;
    }

    private LaunchNode BuildWeekNode(
        DateOnly weekStart,
        IReadOnlyList<DateOnly> days,
        IReadOnlyDictionary<DateOnly, List<SessionRecord>> byDay,
        string keyPrefix)
    {
        var ordered = days.OrderBy(d => d).ToList();
        var first = ordered[0];
        var last = ordered[^1];
        var dateText = first == last
            ? $"{first.Month}月{first.Day}日 周{Fmt.Weekday(first)}"
            : $"{first.Month}月{first.Day}日 – {last.Month}月{last.Day}日";
        var weekStartDate = weekStart.ToDateTime(TimeOnly.MinValue);
        var weekNo = System.Globalization.ISOWeek.GetWeekOfYear(weekStartDate);
        var weekYear = System.Globalization.ISOWeek.GetYear(weekStartDate);
        var weekText = weekYear == first.Year
            ? $"第 {weekNo} 周"
            : $"{weekYear} 年第 {weekNo} 周";
        var title = $"{weekText} · {dateText}";
        var node = new LaunchNode
        {
            Key = $"w:{keyPrefix}:{weekStart:yyyyMMdd}",
            Title = title,
            Level = 1
        };

        foreach (var day in ordered)
        {
            var sessions = byDay.TryGetValue(day, out var list)
                ? list
                : new List<SessionRecord>();
            if (sessions.Count > 0)
                node.Children.Add(BuildDayNode(day, byDay));
        }
        return node;
    }

    private LaunchNode BuildDayNode(
        DateOnly day, IReadOnlyDictionary<DateOnly, List<SessionRecord>> byDay)
    {
        return new LaunchNode
        {
            Key = $"d:{day:yyyyMMdd}",
            Title = $"{day.Month}月{day.Day}日 周{Fmt.Weekday(day)}",
            Level = 2,
            Sessions = byDay.TryGetValue(day, out var sessions)
                ? sessions
                : new List<SessionRecord>()
        };
    }

    private UIElement BuildLaunchRow(SessionRecord session, DateTimeOffset now)
    {
        var localStart = session.Start.ToLocalTime();
        var localEnd = session.End?.ToLocalTime();
        var crossDay = localEnd is not null && localEnd.Value.Date != localStart.Date;
        var endText = session.IsOpen
            ? "运行中"
            : (crossDay ? "次日 " : "") + localEnd?.ToString("HH:mm");
        var duration = session.DurationAt(now).TotalSeconds;

        var row = new Grid { Height = 32, Margin = new Thickness(0, 1, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });

        var times = new TextBlock
        {
            Text = $"{localStart:MM-dd HH:mm} – {endText}",
            FontSize = 10.5,
            Foreground = ColorUtil.Brush("#6F7182"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0)
        };
        row.Children.Add(times);

        var durationText = new TextBlock
        {
            Text = Fmt.Duration(duration),
            FontSize = 10.5,
            FontWeight = FontWeights.Medium,
            Foreground = ColorUtil.Brush("#444555"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(durationText, 1);
        row.Children.Add(durationText);

        var source = new TextBlock
        {
            Text = session.IsImported ? "导入" : "",
            FontSize = 9.5,
            Foreground = ColorUtil.Brush("#9A9CAB"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(source, 2);
        row.Children.Add(source);

        var delete = new Button
        {
            Content = "\uE74D",
            FontFamily = (FontFamily)FindResource("IconFont"),
            FontSize = 11,
            Width = 26,
            Height = 26,
            Tag = session.Id,
            Style = (Style)FindResource("IconButton"),
            ToolTip = session.IsOpen ? "正在运行，结束后才能删除" : "删除这次启动记录",
            IsEnabled = !session.IsOpen
        };
        delete.Click += DeleteLaunch_Click;
        Grid.SetColumn(delete, 3);
        row.Children.Add(delete);

        return row;
    }

    private sealed class LaunchNode
    {
        public string Key { get; init; } = "";
        public string Title { get; init; } = "";
        public int Level { get; init; }
        public List<SessionRecord> Sessions { get; init; } = new();
        public List<LaunchNode> Children { get; init; } = new();

        public int Count() => Sessions.Count + Children.Sum(c => c.Count());

        public double Seconds(DateTimeOffset now) =>
            Sessions.Sum(s => s.DurationAt(now).TotalSeconds) +
            Children.Sum(c => c.Seconds(now));
    }

    private void DeleteLaunch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        var session = App.Store.GetSessions().FirstOrDefault(s => s.Id == id && s.AppId == _appId);
        if (session is null) return;
        if (session.IsOpen)
        {
            ShowToast("正在运行的记录不能删除，请等它结束后再试。", ToastKind.Warning);
            return;
        }

        var localStart = session.Start.ToLocalTime();
        var localEnd = session.End?.ToLocalTime();
        var seconds = session.DurationAt(DateTimeOffset.UtcNow).TotalSeconds;
        var result = System.Windows.MessageBox.Show(
            $"删除这次启动记录？\n\n{localStart:yyyy年M月d日 HH:mm} – {localEnd:HH:mm}\n" +
            $"时长：{Fmt.Duration(seconds)}\n\n此操作无法撤销。",
            "删除启动记录", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            var removed = App.Store.DeleteSessions(new[] { session.Id });
            ShowToast($"已删除 {removed} 次启动记录。", ToastKind.Success);
            RefreshData();
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowToast("删除失败：" + ex.Message, ToastKind.Error);
        }
    }

    private void BuildChart(
        IReadOnlyList<SessionRecord> sessions,
        WatchedApp app,
        PeriodWindow range,
        DateTimeOffset now)
    {
        var buckets = StatsService.BuildBuckets(
            sessions, range.From, range.To, range.BucketCount, range.BucketStart, now,
            new[] { app });
        var bars = new List<ChartBar>();
        for (var i = 0; i < buckets.Count; i++)
        {
            var bucket = buckets[i];
            var segments = bucket.Total > 0
                ? new List<ChartSegment>
                {
                    new() { Name = app.Name, Color = app.Color, Value = bucket.Total }
                }
                : new List<ChartSegment>();
            bars.Add(new ChartBar
            {
                Label = range.BucketLabel(i),
                Tooltip = $"{range.BucketTooltip(i)}\n{app.Name}  {Fmt.Duration(bucket.Total)}",
                Segments = segments
            });
        }
        Chart.SetData(bars);
    }

    private void BuildMonth(
        WatchedApp app,
        IReadOnlyList<SessionRecord> appSessions,
        IReadOnlyList<SessionRecord> allSessions,
        DateTimeOffset now)
    {
        var monthStart = TimeUtil.MonthStart(_monthAnchor);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var from = TimeUtil.LocalDayStart(monthStart);
        var to = TimeUtil.LocalDayEnd(monthEnd);
        MonthTitle.Text = $"{monthStart.Year}年{monthStart.Month}月";

        var dayTotals = StatsService.GetDayTotals(appSessions, monthStart, monthEnd, now);
        var total = dayTotals.Sum(d => d.Seconds);
        var longest = dayTotals.OrderByDescending(d => d.Seconds).FirstOrDefault();
        MonthTotalText.Text = Fmt.Duration(total);
        if (longest is null || longest.Seconds <= 0)
        {
            LongestDayText.Text = "—";
            LongestDaySub.Text = "这个月还没有记录";
        }
        else
        {
            LongestDayText.Text = $"{longest.Day.Month}月{longest.Day.Day}日";
            LongestDaySub.Text = Fmt.Duration(longest.Seconds);
        }

        var allTotal = StatsService.Summarize(allSessions, from, to, now).TotalSeconds;
        ShareText.Text = allTotal <= 0 ? "0%" : $"{total / allTotal * 100:0.00}%";

        CalendarGrid.Children.Clear();
        var first = new DateTime(monthStart.Year, monthStart.Month, 1);
        var leading = ((int)first.DayOfWeek + 6) % 7;
        for (var i = 0; i < leading; i++)
            CalendarGrid.Children.Add(new Border { Height = 34, Margin = new Thickness(2) });

        var max = Math.Max(1, dayTotals.Max(d => d.Seconds));
        foreach (var day in dayTotals)
        {
            var ratio = day.Seconds / max;
            var cell = new Border
            {
                Height = 34,
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(6),
                Background = day.Seconds <= 0
                    ? ColorUtil.Brush("#F5F6FA")
                    : ColorUtil.BlendWithWhite(app.Color, 0.82 - ratio * 0.52),
                ToolTip = day.Seconds <= 0
                    ? $"{day.Day.Month}月{day.Day.Day}日 · 0秒"
                    : $"{day.Day.Month}月{day.Day.Day}日 · {Fmt.Duration(day.Seconds)}"
            };
            var text = new TextBlock
            {
                Text = day.Day.Day.ToString(),
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = ColorUtil.Brush(ratio > 0.55 ? "#FFFFFF" : "#5D6070")
            };
            cell.Child = text;
            CalendarGrid.Children.Add(cell);
        }

        var trailing = 42 - leading - dayTotals.Count;
        for (var i = 0; i < trailing; i++)
            CalendarGrid.Children.Add(new Border { Height = 34, Margin = new Thickness(2) });
    }

    private void ClearMonth_Click(object sender, RoutedEventArgs e)
    {
        var monthStart = TimeUtil.MonthStart(_monthAnchor);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var from = TimeUtil.LocalDayStart(monthStart);
        var to = TimeUtil.LocalDayEnd(monthEnd);
        var now = DateTimeOffset.UtcNow;
        var sessions = App.Store.GetSessions()
            .Where(s => s.AppId == _appId && !s.IsOpen &&
                        TimeUtil.Overlaps(s.Start, s.End, from, to, now))
            .ToList();
        if (sessions.Count == 0)
        {
            ShowToast("这个月没有可删除的历史记录。", ToastKind.Warning);
            return;
        }

        var seconds = sessions.Sum(s => s.DurationAt(now).TotalSeconds);
        var result = System.Windows.MessageBox.Show(
            $"确定清空「{AppNameText.Text}」在 {monthStart.Year}年{monthStart.Month}月的 {sessions.Count} 条记录吗？\n\n" +
            $"合计时长：{Fmt.Duration(seconds)}\n\n此操作无法撤销。",
            "清空该月统计", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;
        DeleteSessions(sessions.Select(s => s.Id), "已清空该月统计。");
    }

    private void DeleteAll_Click(object sender, RoutedEventArgs e)
    {
        var sessions = App.Store.GetSessions()
            .Where(s => s.AppId == _appId && !s.IsOpen)
            .ToList();
        if (sessions.Count == 0)
        {
            ShowToast("这个应用没有可删除的历史记录。", ToastKind.Warning);
            return;
        }

        var seconds = sessions.Sum(s => s.DurationAt(DateTimeOffset.UtcNow).TotalSeconds);
        var result = System.Windows.MessageBox.Show(
            $"确定删除「{AppNameText.Text}」的全部 {sessions.Count} 条历史记录吗？\n\n" +
            $"合计时长：{Fmt.Duration(seconds)}\n运行中的记录会保留。\n\n此操作无法撤销。",
            "删除全部记录", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;
        DeleteSessions(sessions.Select(s => s.Id), "已删除全部历史记录。");
    }

    private void DeleteSessions(IEnumerable<string> ids, string toast)
    {
        try
        {
            var removed = App.Store.DeleteSessions(ids);
            ShowToast($"{toast}（{removed} 条）", ToastKind.Success);
            RefreshData();
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowToast("删除失败：" + ex.Message, ToastKind.Error);
        }
    }

    public void OnTick()
    {
        // Keep this page stable while it is open. Refreshing on the one-second
        // clock rebuilt the launch tree and reset the user's expanded groups.
    }

    private void ShowToast(string message, ToastKind kind)
    {
        if (Window.GetWindow(this) is MainWindow main) main.ShowToast(message, kind);
    }
}
