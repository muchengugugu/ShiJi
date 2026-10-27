using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ShiJi.Models;
using ShiJi.Services;

namespace ShiJi.Views;

public partial class CalendarView : UserControl, IRefreshablePage
{
    private bool _ready;
    private string _range = "14";
    private DateOnly _selectedDay = TimeUtil.Today;
    private string? _selectedAppId;
    private DateTime _lastRefresh = DateTime.MinValue;
    private DateOnly _lastDay = TimeUtil.Today;
    private double _matrixWidth;

    public CalendarView()
    {
        InitializeComponent();
        _ready = true;
        MatrixScroll.SizeChanged += (_, e) =>
        {
            if (!_ready || e.NewSize.Width < 300) return;
            if (Math.Abs(e.NewSize.Width - _matrixWidth) < 20) return;
            _matrixWidth = e.NewSize.Width;
            RefreshData();
        };
        TodayText.Text = TimeUtil.Today.ToString("yyyy年M月d日") + " 周" + Fmt.Weekday(TimeUtil.Today);
        Loaded += (_, _) => Dispatcher.BeginInvoke(
            new Action(() => PageScroll.ScrollToTop()), DispatcherPriority.Loaded);
    }

    private void Range_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready || sender is not RadioButton { Tag: string tag }) return;
        _range = tag;
        RefreshData();
    }

    private void MatrixScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        PageScroll.ScrollToVerticalOffset(PageScroll.VerticalOffset - e.Delta);
    }

    public void RefreshData()
    {
        if (!_ready) return;
        _lastRefresh = DateTime.Now;
        var today = TimeUtil.Today;
        if (_lastDay != today)
        {
            _lastDay = today;
            _selectedDay = today;
        }
        if (_selectedDay > today) _selectedDay = today;
        TodayText.Text = TimeUtil.Today.ToString("yyyy年M月d日") + " 周" + Fmt.Weekday(today);

        var now = DateTimeOffset.UtcNow;
        var apps = App.Store.GetApps();
        var sessions = App.Store.GetSessions();
        var running = App.Tracker.GetRunningAppIds();
        var days = GetRangeDays(today);
        var status = StatsService.GetDayStatus(apps, sessions, today, now, running)
            .OrderByDescending(s => s.Running)
            .ThenByDescending(s => s.Launched)
            .ThenByDescending(s => s.TodaySeconds)
            .ToList();
        var displayStatus = status.Where(s => s.App.ShowInToday).ToList();
        var displayApps = displayStatus.Select(s => s.App).ToList();
        var displayIds = displayApps.Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var matrix = StatsService.GetMatrix(displayApps, sessions, days[0], days[^1], now);

        var todayLaunched = displayStatus.Count(s => s.Launched);
        var todayLaunches = displayStatus.Sum(s => s.Launches);
        var rangeSessions = sessions
            .Where(s => displayIds.Contains(s.AppId))
            .ToList();
        var rangeLaunches = rangeSessions.Count(s =>
            TimeUtil.LocalDate(s.Start) >= days[0] && TimeUtil.LocalDate(s.Start) <= days[^1]);
        var rangeDaysWithLaunch = rangeSessions
            .Where(s => TimeUtil.LocalDate(s.Start) >= days[0] && TimeUtil.LocalDate(s.Start) <= days[^1])
            .Select(s => TimeUtil.LocalDate(s.Start))
            .Distinct()
            .Count();
        var rangeSummary = StatsService.Summarize(
            rangeSessions, TimeUtil.LocalDayStart(days[0]), TimeUtil.LocalDayEnd(days[^1]), now,
            displayApps, running.Count);

        TodayLaunchedValue.Text = $"{todayLaunched} / {displayApps.Count}";
        TodayLaunchedSub.Text = displayApps.Count == 0
            ? "没有显示的应用"
            : todayLaunched == displayApps.Count ? "全部启动过" : "显示中的应用";
        TodayLaunchCountValue.Text = $"{todayLaunches} 次";
        TodayLaunchCountSub.Text = todayLaunches > 0 ? $"首次 {FirstLaunchText(displayStatus)}" : "今天还没有启动";
        RangeLaunchLabel.Text = RangeName() + "启动";
        RangeLaunchValue.Text = $"{rangeLaunches} 次";
        RangeLaunchSub.Text = $"覆盖 {rangeDaysWithLaunch} 天";
        RangeDurationValue.Text = Fmt.Duration(rangeSummary.TotalSeconds);
        RangeDurationSub.Text = $"{rangeSummary.AppCount} 个应用有运行";
        TodayRatioText.Text = $"{todayLaunched} / {displayApps.Count}";
        TodayAddButton.Visibility = apps.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        BuildTodayPanel(displayApps, displayStatus);
        BuildMatrix(displayApps, matrix, days, today, displayApps.Count);
        BuildDetail(apps, sessions, now);
    }

    private List<DateOnly> GetRangeDays(DateOnly today)
    {
        return _range switch
        {
            "30" => TimeUtil.Days(today.AddDays(-29), today).ToList(),
            "month" => TimeUtil.Days(TimeUtil.MonthStart(today), today).ToList(),
            _ => TimeUtil.Days(today.AddDays(-13), today).ToList()
        };
    }

    private string RangeName() => _range switch
    {
        "30" => "近 30 天",
        "month" => "本月",
        _ => "近 14 天"
    };

    private static string FirstLaunchText(IReadOnlyList<DayAppStatus> status)
    {
        var first = status.Where(s => s.FirstLaunch is not null)
            .Select(s => s.FirstLaunch!.Value)
            .OrderBy(t => t)
            .FirstOrDefault();
        return first == default ? "—" : Fmt.Clock(first);
    }

    private void BuildTodayPanel(IReadOnlyList<WatchedApp> apps, IReadOnlyList<DayAppStatus> status)
    {
        TodayPanel.Children.Clear();
        TodayEmpty.Visibility = apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TodayPanel.Visibility = apps.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        foreach (var item in status)
        {
            var card = new Border
            {
                Width = 164,
                MinHeight = 76,
                Margin = new Thickness(0, 0, 9, 9),
                Padding = new Thickness(10, 10, 9, 10),
                CornerRadius = new CornerRadius(11),
                BorderThickness = new Thickness(1),
                BorderBrush = ColorUtil.Brush(item.Launched || item.Running ? "#E6E1FF" : "#EFEFF5"),
                Background = ColorUtil.Brush(item.Running ? "#FBFAFF" : "#FFFFFF")
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var badge = Ui.AppBadge(item.App, 30);
            badge.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(badge, 0);
            grid.Children.Add(badge);

            var text = new StackPanel { Margin = new Thickness(2, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock
            {
                Text = item.App.Name,
                FontSize = 11.5,
                FontWeight = FontWeights.Medium,
                Foreground = ColorUtil.Brush(item.App.Enabled ? "#1D1E2A" : "#9A9CAB"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 108
            });

            var sub = item.Launched && item.FirstLaunch is not null
                ? $"首次 {Fmt.Clock(item.FirstLaunch)}"
                : item.Running ? "跨天运行中" : "今天未启动";
            text.Children.Add(new TextBlock
            {
                Text = sub,
                FontSize = 9.5,
                Foreground = ColorUtil.Brush("#9A9CAB"),
                Margin = new Thickness(0, 3, 0, 0)
            });

            var pill = Ui.StatusPill(item.Launched, item.Running);
            pill.Margin = new Thickness(0, 5, 0, 0);
            text.Children.Add(pill);

            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            card.Child = grid;
            TodayPanel.Children.Add(card);
        }
    }

    private void BuildMatrix(
        IReadOnlyList<WatchedApp> apps,
        IReadOnlyList<AppDayCell> matrix,
        IReadOnlyList<DateOnly> days,
        DateOnly today,
        int totalAppCount)
    {
        MatrixGrid.Children.Clear();
        MatrixGrid.ColumnDefinitions.Clear();
        MatrixGrid.RowDefinitions.Clear();

        if (apps.Count == 0)
        {
            MatrixGrid.Children.Add(new TextBlock
            {
                Text = "添加应用后，这里会按天展示每个应用的启动情况。",
                FontSize = 11.5,
                Foreground = ColorUtil.Brush("#9A9CAB"),
                Margin = new Thickness(2, 14, 0, 14)
            });
            return;
        }

        var labelWidth = 170.0;
        var available = MatrixScroll.ActualWidth > 300 ? MatrixScroll.ActualWidth - 4 : 900;
        var colWidth = Math.Clamp(
            (available - labelWidth) / days.Count,
            _range == "14" ? 32 : 18,
            60);
        var rowHeight = _range == "14" ? 43.0 : 36.0;
        MatrixGrid.Width = Math.Max(available, labelWidth + colWidth * days.Count);
        MatrixGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
        for (var i = 0; i < days.Count; i++)
            MatrixGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(colWidth) });
        MatrixGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(_range == "14" ? 51 : 46) });
        for (var i = 0; i < apps.Count; i++)
            MatrixGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(rowHeight) });
        MatrixGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });

        var corner = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(2, 0, 0, 6) };
        corner.Children.Add(new TextBlock { Text = "应用 / 日期", FontSize = 10.5, Foreground = ColorUtil.Brush("#9A9CAB") });
        Grid.SetRow(corner, 0);
        Grid.SetColumn(corner, 0);
        MatrixGrid.Children.Add(corner);

        for (var d = 0; d < days.Count; d++)
        {
            var day = days[d];
            var isToday = day == today;
            var head = new Border
            {
                Margin = new Thickness(1, 4, 1, 5),
                CornerRadius = new CornerRadius(7),
                Background = isToday ? ColorUtil.Brush("#F0EEFF") : Brushes.Transparent,
                Child = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = _range == "14" ? $"{day.Month}/{day.Day}" : day.Day.ToString(),
                            FontSize = _range == "14" ? 10 : 8.5,
                            FontWeight = isToday ? FontWeights.SemiBold : FontWeights.Normal,
                            Foreground = ColorUtil.Brush(isToday ? "#5A49D6" : "#6F7182"),
                            HorizontalAlignment = HorizontalAlignment.Center
                        },
                        new TextBlock
                        {
                            Text = "日一二三四五六"[(int)day.DayOfWeek].ToString(),
                            FontSize = 8.5,
                            Foreground = ColorUtil.Brush(isToday ? "#6A5AE0" : "#B0B2C0"),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            Margin = new Thickness(0, 1, 0, 0)
                        }
                    }
                }
            };
            Grid.SetRow(head, 0);
            Grid.SetColumn(head, d + 1);
            MatrixGrid.Children.Add(head);
        }

        var lookup = matrix.ToDictionary(c => (c.AppId, c.Day));

        for (var r = 0; r < apps.Count; r++)
        {
            var app = apps[r];
            var label = new Grid
            {
                Margin = new Thickness(0, 0, 8, 0),
                ToolTip = $"{app.Name}\n{app.ExecutablePath}\n右键可重命名"
            };
            var menu = new ContextMenu();
            var renameItem = new MenuItem { Header = "重命名此应用" };
            renameItem.Click += (_, _) => RenameApp(app);
            menu.Items.Add(renameItem);
            label.ContextMenu = menu;
            label.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            label.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var badge = Ui.AppBadge(app, 22);
            badge.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(badge, 0);
            label.Children.Add(badge);
            var name = new TextBlock
            {
                Text = app.Name,
                FontSize = 11,
                Foreground = ColorUtil.Brush(app.Enabled ? "#3F4050" : "#9A9CAB"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(name, 1);
            label.Children.Add(name);
            Grid.SetRow(label, r + 1);
            Grid.SetColumn(label, 0);
            MatrixGrid.Children.Add(label);

            for (var d = 0; d < days.Count; d++)
            {
                lookup.TryGetValue((app.Id, days[d]), out var cell);
                var launches = cell?.Launches ?? 0;
                var bg = launches > 0
                    ? ColorUtil.Brush("#6A5AE0")
                    : Brushes.White;
                var fg = launches > 0
                    ? Brushes.White
                    : ColorUtil.Brush("#B6B8C5");
                var isSelected = _selectedDay == days[d] &&
                                 (string.IsNullOrEmpty(_selectedAppId) || _selectedAppId == app.Id);
                var isToday = days[d] == today;

                var text = launches > 0 ? (launches > 9 ? "9+" : launches.ToString()) : "·";
                var border = new Border
                {
                    Margin = new Thickness(2),
                    CornerRadius = new CornerRadius(6),
                    Background = bg,
                    BorderBrush = isSelected
                        ? ColorUtil.Brush("#4E3FC7")
                        : isToday ? ColorUtil.Brush("#BDB4FF")
                        : launches > 0 ? Brushes.Transparent : ColorUtil.Brush("#E8E8F0"),
                    BorderThickness = new Thickness(isSelected ? 1.6 : isToday ? 1 : launches > 0 ? 0 : 1),
                    Cursor = Cursors.Hand,
                    Tag = new CellTag(app.Id, days[d], app.Name),
                    ToolTip = BuildCellTooltip(app, days[d], launches, cell?.FirstLaunch)
                };
                border.Child = new TextBlock
                {
                    Text = text,
                    FontSize = _range == "14" ? 9.5 : 8.5,
                    FontWeight = launches > 0 ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = fg,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                border.MouseLeftButtonUp += Cell_Click;
                Grid.SetRow(border, r + 1);
                Grid.SetColumn(border, d + 1);
                MatrixGrid.Children.Add(border);
            }
        }

        var totalRow = apps.Count + 1;
        var totalLabel = new TextBlock
        {
            Text = apps.Count < totalAppCount ? "显示合计" : "名单合计",
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = ColorUtil.Brush("#6F7182"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 0, 0)
        };
        Grid.SetRow(totalLabel, totalRow);
        Grid.SetColumn(totalLabel, 0);
        MatrixGrid.Children.Add(totalLabel);

        for (var d = 0; d < days.Count; d++)
        {
            var dayCells = matrix.Where(c => c.Day == days[d]).ToList();
            var launches = dayCells.Sum(c => c.Launches);
            var text = launches > 0 ? launches.ToString() : "·";
            var total = new Border
            {
                Margin = new Thickness(2, 5, 2, 0),
                CornerRadius = new CornerRadius(6),
                Background = ColorUtil.Brush("#F7F6FF"),
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 9,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = ColorUtil.Brush(launches > 0 ? "#6A5AE0" : "#C0C2CE"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                },
                ToolTip = $"{days[d].Month}月{days[d].Day}日\n启动 {launches} 次"
            };
            Grid.SetRow(total, totalRow);
            Grid.SetColumn(total, d + 1);
            MatrixGrid.Children.Add(total);
        }
    }

    private void Cell_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: CellTag tag }) return;
        _selectedAppId = tag.AppId;
        _selectedDay = tag.Day;
        RefreshData();    }

    private void BuildDetail(IReadOnlyList<WatchedApp> apps, IReadOnlyList<SessionRecord> sessions, DateTimeOffset now)
    {
        var app = string.IsNullOrEmpty(_selectedAppId)
            ? null
            : apps.FirstOrDefault(a => a.Id == _selectedAppId);
        var day = _selectedDay;
        var dayStart = TimeUtil.LocalDayStart(day);
        var dayEnd = TimeUtil.LocalDayEnd(day);

        var daySessions = sessions
            .Where(s => string.IsNullOrEmpty(_selectedAppId) || s.AppId == _selectedAppId)
            .Where(s => TimeUtil.Overlaps(s.Start, s.End, dayStart, dayEnd, now))
            .OrderBy(s => s.Start)
            .ToList();

        var launches = daySessions.Count(s => s.Start >= dayStart && s.Start < dayEnd);
        var seconds = daySessions.Sum(s => TimeUtil.OverlapSeconds(s.Start, s.End, dayStart, dayEnd, now));

        DetailTitle.Text = $"{day.Month}月{day.Day}日 周{Fmt.Weekday(day)}" + (app is null ? " · 全部应用" : " · " + app.Name);
        DetailSubtitle.Text = $"当天启动 {launches} 次 · 运行 {Fmt.Duration(seconds)} · 共 {daySessions.Count} 段记录";
        DetailResetButton.Content = day == TimeUtil.Today && _selectedAppId is null ? "今天" : "查看今日";
        DetailResetButton.IsEnabled = !(day == TimeUtil.Today && _selectedAppId is null);

        DetailPanel.Children.Clear();
        DetailEmpty.Visibility = daySessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DetailEmptyText.Text = _selectedAppId is null
            ? "这一天没有应用运行记录。"
            : $"{app?.Name ?? "这个应用"} 在这一天没有运行记录。";

        var shown = daySessions.ToList();
        foreach (var session in shown)
        {
            var sessionApp = apps.FirstOrDefault(a => a.Id == session.AppId);
            var name = sessionApp?.Name ?? session.AppName;
            var color = sessionApp?.Color ?? session.AppColor;
            var row = new Grid { Height = 43 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });

            var badge = Ui.AppBadge(name, color, sessionApp?.ExecutablePath, 24);
            Grid.SetColumn(badge, 0);
            row.Children.Add(badge);

            var nameText = new TextBlock
            {
                Text = name,
                FontSize = 11.5,
                FontWeight = FontWeights.Medium,
                Foreground = ColorUtil.Brush("#303140"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 10, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(nameText, 1);
            row.Children.Add(nameText);

            var crosses = session.Start < dayStart;
            var startText = (crosses ? "跨天 · " : "") + Fmt.Clock(session.Start) + " → " +
                            (session.IsOpen ? "运行中" : Fmt.Clock(session.End));
            var times = new TextBlock
            {
                Text = startText,
                FontSize = 10.5,
                Foreground = ColorUtil.Brush("#77798A"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(times, 2);
            row.Children.Add(times);

            var duration = new TextBlock
            {
                Text = Fmt.Duration(TimeUtil.OverlapSeconds(session.Start, session.End, dayStart, dayEnd, now)),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = ColorUtil.Brush("#444555"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(duration, 3);
            row.Children.Add(duration);

            var state = session.IsOpen ? Ui.RunningPill(true) : Ui.Pill("已结束", "#7C7E90", "#F3F3F7", "\uE73E");
            state.HorizontalAlignment = HorizontalAlignment.Left;
            state.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(state, 4);
            row.Children.Add(state);

            var delete = new Button
            {
                Content = "\uE74D",
                FontFamily = (FontFamily)FindResource("IconFont"),
                FontSize = 11,
                Width = 25,
                Height = 25,
                Tag = session.Id,
                Style = (Style)FindResource("IconButton"),
                ToolTip = session.IsOpen ? "正在运行，结束后才能删除" : "删除这次启动记录",
                IsEnabled = !session.IsOpen
            };
            delete.Click += DeleteDetailSession_Click;
            Grid.SetColumn(delete, 5);
            row.Children.Add(delete);

            DetailPanel.Children.Add(row);
            DetailPanel.Children.Add(new Border { Height = 1, Background = ColorUtil.Brush("#F4F4F8") });
        }
    }

    private void DeleteDetailSession_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        var session = App.Store.GetSession(id);
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

    private void DetailReset_Click(object sender, RoutedEventArgs e)
    {
        _selectedDay = TimeUtil.Today;
        _selectedAppId = null;
        RefreshData();
    }

    private void RenameApp(WatchedApp app)
    {
        try
        {
            var dialog = new EditAppDialog(app) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() != true) return;
            App.Store.UpdateApp(app.Id, a =>
            {
                a.Name = dialog.AppName;
                a.Color = dialog.AppColor;
            });
            RefreshData();
            if (Window.GetWindow(this) is MainWindow main)
                main.ShowToast("应用名称已更新。", ToastKind.Success);
        }
        catch (Exception ex)
        {
            App.Log(ex);
            if (Window.GetWindow(this) is MainWindow main)
                main.ShowToast("重命名失败：" + ex.Message, ToastKind.Error);
        }
    }

    private void AddApp_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow main) main.OpenWatchlist().BeginAdd();
    }

    public void OnTick()
    {
        if (!_ready) return;
        if (TimeUtil.Today != _lastDay)
        {
            RefreshData();
            return;
        }
        if (App.Tracker.RunningAppCount <= 0) return;
        if (MatrixScroll.IsMouseOver || TodayPanel.IsMouseOver) return;
        if ((DateTime.Now - _lastRefresh).TotalSeconds < 45) return;
        RefreshData();
    }

    private void ShowToast(string message, ToastKind kind)
    {
        if (Window.GetWindow(this) is MainWindow main)
            main.ShowToast(message, kind);
    }

    private static string BuildCellTooltip(WatchedApp app, DateOnly day, int launches, DateTimeOffset? first)
    {
        var lines = new List<string>
        {
            $"{day.Month}月{day.Day}日 周{Fmt.Weekday(day)}",
            app.Name,
            $"启动 {launches} 次"
        };
        if (first is not null) lines.Add("首次 " + Fmt.Clock(first));
        return string.Join("\n", lines);
    }

    private sealed record CellTag(string AppId, DateOnly Day, string AppName);
}
