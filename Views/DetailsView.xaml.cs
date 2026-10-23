using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ShiJi.Models;
using ShiJi.Services;

namespace ShiJi.Views;

public partial class DetailsView : UserControl, IRefreshablePage
{
    private StatsPeriod _period = StatsPeriod.Day;
    private DateOnly _anchor = TimeUtil.Today;
    private string _search = "";
    private bool _sortDurationDescending = true;
    private bool _ready;
    private DateTime _lastRefresh = DateTime.MinValue;

    private string CurrentPeriodLabel => _period switch
    {
        StatsPeriod.Week => "按周",
        StatsPeriod.Month => "按月",
        StatsPeriod.Year => "按年",
        _ => "按天"
    };

    public DetailsView()
    {
        InitializeComponent();
        _ready = true;
        Loaded += (_, _) => Dispatcher.BeginInvoke(
            new Action(() => PageScroll.ScrollToTop()),
            System.Windows.Threading.DispatcherPriority.Loaded);
        RefreshData();
    }

    public event Action<string, StatsPeriod, DateOnly>? AppSelected;

    private void Period_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready || sender is not RadioButton { Tag: string tag }) return;
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
        RefreshData();
    }

    private void DurationSort_Click(object sender, RoutedEventArgs e)
    {
        _sortDurationDescending = !_sortDurationDescending;
        RefreshData();
    }

    private void UpdateSortVisual()
    {
        DurationSortGlyph.Text = _sortDurationDescending ? "↓" : "↑";
        DurationSortButton.Foreground = ColorUtil.Brush("#5A49D6");
        DurationSortButton.Background = ColorUtil.Brush("#F0EEFF");
        DurationSortButton.ToolTip = _sortDurationDescending
            ? "当前从长到短，点击切换为从短到长"
            : "当前从短到长，点击切换为从长到短";
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _search = SearchBox.Text.Trim();
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        RefreshData();
    }

    public void RefreshData()
    {
        if (!_ready) return;
        _lastRefresh = DateTime.Now;
        if (_anchor > TimeUtil.Today) _anchor = TimeUtil.Today;
        var now = DateTimeOffset.UtcNow;
        var range = Periods.Build(_period, _anchor);
        var apps = App.Store.GetApps();
        var sessions = App.Store.GetSessions();
        var aggregate = StatsService.AggregateApps(sessions, range.From, range.To, now, apps)
            .Where(a => a.Seconds > 0)
            .ToList();
        if (_search.Length > 0)
        {
            aggregate = aggregate
                .Where(a => a.Name.Contains(_search, StringComparison.CurrentCultureIgnoreCase))
                .ToList();
        }

        aggregate.Sort((a, b) =>
        {
            var compare = _sortDurationDescending
                ? b.Seconds.CompareTo(a.Seconds)
                : a.Seconds.CompareTo(b.Seconds);
            return compare != 0
                ? compare
                : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
        });
        UpdateSortVisual();

        PeriodLabel.Text = CurrentPeriodLabel;
        RangeText.Text = range.Label;
        TotalAppsText.Text = $"总计 {aggregate.Count} 个";
        EmptyText.Visibility = aggregate.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        AppListPanel.Children.Clear();
        if (aggregate.Count == 0) return;

        var max = aggregate[0].Seconds;
        foreach (var item in aggregate)
            AppListPanel.Children.Add(BuildRow(item, apps, max));
    }

    private UIElement BuildRow(AppAggregate item, IReadOnlyList<WatchedApp> apps, double max)
    {
        var app = apps.FirstOrDefault(a => a.Id == item.AppId);
        var row = new Border
        {
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 7, 8, 7),
            Margin = new Thickness(0, 1, 0, 1),
            Cursor = Cursors.Hand,
            Tag = item.AppId
        };
        row.PreviewMouseLeftButtonUp += Row_Click;
        row.MouseEnter += (_, _) => row.Background = ColorUtil.Brush("#F8F8FC");
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        row.Child = grid;

        var badge = Ui.AppBadge(item.Name, item.Color, app?.ExecutablePath, 34);
        badge.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(badge);

        var info = new StackPanel { Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock
        {
            Text = item.Name,
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = ColorUtil.Brush("#252633"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = item.Name
        };
        info.Children.Add(title);

        var meta = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
        meta.Children.Add(new TextBlock
        {
            Text = app is null ? "应用已移出名单" : app.ExecutablePath,
            FontSize = 10,
            Foreground = ColorUtil.Brush("#9A9CAB"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 420,
            ToolTip = app?.ExecutablePath
        });
        info.Children.Add(meta);
        Grid.SetColumn(info, 1);
        grid.Children.Add(info);

        var track = new Grid { Height = 7, Width = 190, VerticalAlignment = VerticalAlignment.Center };
        track.Children.Add(new Border { Background = ColorUtil.Brush("#F0F1F6"), CornerRadius = new CornerRadius(4) });
        track.Children.Add(new Border
        {
            Background = ColorUtil.Brush(item.Color),
            CornerRadius = new CornerRadius(4),
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = max <= 0 ? 0 : 190 * Math.Clamp(item.Seconds / max, 0.02, 1)
        });
        Grid.SetColumn(track, 2);
        grid.Children.Add(track);

        var duration = new TextBlock
        {
            Text = Fmt.Duration(item.Seconds),
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = ColorUtil.Brush("#2C2D3A"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(duration, 3);
        grid.Children.Add(duration);

        var delete = new Button
        {
            Content = "\uE74D",
            Style = (Style)Application.Current.FindResource("IconButton"),
            Width = 26,
            Height = 26,
            FontSize = 11,
            Foreground = ColorUtil.Brush("#D06B78"),
            ToolTip = "删除该应用的全部历史记录",
            Tag = item.AppId,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        delete.Click += Delete_Click;
        Grid.SetColumn(delete, 4);
        grid.Children.Add(delete);

        var chevron = new TextBlock
        {
            Text = "\uE76C",
            FontFamily = (FontFamily)Application.Current.FindResource("IconFont"),
            FontSize = 10,
            Foreground = ColorUtil.Brush("#B3B5C4"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Grid.SetColumn(chevron, 5);
        grid.Children.Add(chevron);
        return row;
    }

    private void Row_Click(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null) return;
        if (sender is Border { Tag: string appId })
            AppSelected?.Invoke(appId, _period, _anchor);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string appId }) return;
        var sessions = App.Store.GetSessions()
            .Where(s => s.AppId == appId && !s.IsOpen)
            .ToList();
        if (sessions.Count == 0)
        {
            ShowToast("这个应用没有可删除的历史记录。", ToastKind.Warning);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var seconds = sessions.Sum(s => s.DurationAt(now).TotalSeconds);
        var app = App.Store.GetApp(appId);
        var name = app?.Name ?? sessions[0].AppName;
        var result = System.Windows.MessageBox.Show(
            $"确定删除「{name}」的全部 {sessions.Count} 条历史记录吗？\n\n" +
            $"合计时长：{Fmt.Duration(seconds)}\n" +
            "运行中的记录会保留。\n\n此操作无法撤销。",
            "删除该应用的历史记录", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            var removed = App.Store.DeleteSessions(sessions.Select(s => s.Id));
            ShowToast($"已删除 {removed} 条历史记录。", ToastKind.Success);
            RefreshData();
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowToast("删除失败：" + ex.Message, ToastKind.Error);
        }
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match) return match;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private void ShowToast(string message, ToastKind kind)
    {
        if (Window.GetWindow(this) is MainWindow main) main.ShowToast(message, kind);
    }

    public void OnTick()
    {
        if (App.Tracker.RunningAppCount <= 0) return;
        if ((DateTime.Now - _lastRefresh).TotalSeconds < 10) return;
        RefreshData();
    }
}
