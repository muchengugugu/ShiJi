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
    private DateTime _lastRefresh = DateTime.MinValue;

    public AppDetailView()
    {
        InitializeComponent();
        _ready = true;
        Loaded += (_, _) => Dispatcher.BeginInvoke(
            new Action(() => PageScroll.ScrollToTop()),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    public event Action? BackRequested;

    public void LoadApp(string appId, StatsPeriod period, DateOnly anchor)
    {
        _appId = appId;
        _period = period;
        _anchor = anchor > TimeUtil.Today ? TimeUtil.Today : anchor;
        _monthAnchor = TimeUtil.MonthStart(_anchor == default ? TimeUtil.Today : _anchor);
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
        RefreshData();
    }

    private void Previous_Click(object sender, RoutedEventArgs e)
    {
        _anchor = Periods.Shift(_period, _anchor, -1);
        RefreshData();
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        var next = Periods.Shift(_period, _anchor, 1);
        _anchor = next > TimeUtil.Today ? TimeUtil.Today : next;
        RefreshData();
    }

    private void Today_Click(object sender, RoutedEventArgs e)
    {
        _anchor = TimeUtil.Today;
        _monthAnchor = TimeUtil.MonthStart(_anchor);
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
        _lastRefresh = DateTime.Now;
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
        if (!App.Tracker.GetRunningAppIds().Contains(_appId)) return;
        if ((DateTime.Now - _lastRefresh).TotalSeconds < 10) return;
        RefreshData();
    }

    private void ShowToast(string message, ToastKind kind)
    {
        if (Window.GetWindow(this) is MainWindow main) main.ShowToast(message, kind);
    }
}
