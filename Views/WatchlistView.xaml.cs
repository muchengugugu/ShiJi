using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using ShiJi.Models;
using ShiJi.Services;

namespace ShiJi.Views;

public partial class WatchlistView : UserControl, IRefreshablePage
{
    private readonly List<AppRowVm> _rows = new();
    private bool _ready;
    private string _search = "";
    private string _runningSignature = "";
    private DateTime _lastRefresh = DateTime.MinValue;

    public WatchlistView()
    {
        InitializeComponent();
        _ready = true;
    }

    public void BeginAdd()
    {
        AddApps_Click(this, new RoutedEventArgs());
    }

    public void RefreshData()
    {
        if (!_ready) return;
        _lastRefresh = DateTime.Now;
        var now = DateTimeOffset.UtcNow;
        var apps = App.Store.GetApps();
        var sessions = App.Store.GetSessions();
        var running = App.Tracker.GetRunningAppIds();
        var status = StatsService.GetDayStatus(apps, sessions, TimeUtil.Today, now, running)
            .ToDictionary(s => s.App.Id, StringComparer.OrdinalIgnoreCase);

        _rows.Clear();
        var filtered = apps
            .Where(a => string.IsNullOrWhiteSpace(_search) ||
                        a.Name.Contains(_search, StringComparison.OrdinalIgnoreCase) ||
                        a.ExecutablePath.Contains(_search, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var app in filtered)
        {
            status.TryGetValue(app.Id, out var dayStatus);
            _rows.Add(new AppRowVm(app, dayStatus, running.Contains(app.Id)));
        }

        AppList.ItemsSource = null;
        AppList.ItemsSource = _rows;
        _runningSignature = string.Join("|", running.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));

        SummaryText.Text = $"{apps.Count} 个应用 · {running.Count} 个正在运行";
        FilterCountText.Text = string.IsNullOrWhiteSpace(_search) ? "" : $"匹配 {_rows.Count} 个";
        SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;

        var empty = _rows.Count == 0;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (empty)
        {
            if (apps.Count == 0)
            {
                EmptyTitle.Text = "名单还是空的";
                EmptySub.Text = "添加常用的程序，时迹会在后台记录它们的存在时长和每天的启动情况。";
                EmptyActions.Visibility = Visibility.Visible;
            }
            else
            {
                EmptyTitle.Text = "没有找到匹配的应用";
                EmptySub.Text = "换一个应用名称或路径关键词试试。";
                EmptyActions.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        _search = SearchBox.Text.Trim();
        RefreshData();
    }

    private void AddApps_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择要监控的程序",
            Filter = "可执行程序|*.exe|所有文件|*.*",
            Multiselect = true,
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != true) return;
        AddPaths(dialog.FileNames);
    }

    private void AddRunning_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new RunningAppsDialog(App.Store.GetApps())
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() != true) return;
        AddDetected(dialog.SelectedApps);
    }

    private void AddDetected(IReadOnlyList<DetectedApp> apps)
    {
        try
        {
            var result = App.Store.AddDetectedApps(apps);
            App.Tracker.Kick();
            RefreshData();
            if (result.Added > 0)
                ShowToast($"已加入 {result.Added} 个应用。", ToastKind.Success);
            if (result.Duplicates > 0)
                ShowToast($"有 {result.Duplicates} 个应用已经在名单里。", ToastKind.Warning);
            if (result.Added == 0 && result.Duplicates == 0)
                ShowToast("没有添加新的应用。", ToastKind.Warning);
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowToast("添加失败：" + ex.Message, ToastKind.Error);
        }
    }

    private void AddPaths(IEnumerable<string> paths)
    {
        try
        {
            var result = App.Store.AddApps(paths);
            App.Tracker.Kick();
            RefreshData();
            if (result.Added > 0)
                ShowToast($"已加入 {result.Added} 个应用。", ToastKind.Success);
            if (result.Duplicates > 0)
                ShowToast($"有 {result.Duplicates} 个应用已经在名单里。", ToastKind.Warning);
            if (result.Added == 0 && result.Duplicates == 0)
                ShowToast("没有添加新的应用。", ToastKind.Warning);
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowToast("添加失败：" + ex.Message, ToastKind.Error);
        }
    }

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: string id } check) return;
        var shown = check.IsChecked == true;
        try
        {
            App.Store.SetAppShown(id, shown);
            RefreshData();
            ShowToast(shown ? "已显示在今日启动。" : "已从今日启动隐藏，记录仍会继续。",
                shown ? ToastKind.Success : ToastKind.Info);
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowToast("修改失败：" + ex.Message, ToastKind.Error);
            RefreshData();
        }
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        try
        {
            var app = App.Store.GetApp(id);
            if (app is null) return;
            var dialog = new EditAppDialog(app) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() != true) return;
            App.Store.UpdateApp(id, a =>
            {
                a.Name = dialog.AppName;
                a.Color = dialog.AppColor;
            });
            RefreshData();
            ShowToast("应用信息已更新。", ToastKind.Success);
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowToast("打开编辑窗口失败：" + ex.Message, ToastKind.Error);
        }
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        var app = App.Store.GetApp(id);
        if (app is null) return;
        var result = System.Windows.MessageBox.Show(
            $"把“{app.Name}”移出名单？\n\n历史记录会保留；如果它正在运行，当前这段记录会在这里结束。",
            "移出名单", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        App.Store.RemoveApp(id, deleteHistory: false);
        App.Tracker.Kick();
        RefreshData();
        ShowToast("已移出名单，历史记录仍保留。", ToastKind.Success);
    }

    public void OnTick()
    {
        if (!_ready) return;
        var running = App.Tracker.GetRunningAppIds();
        var signature = string.Join("|", running.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        if (signature != _runningSignature)
        {
            RefreshData();
            return;
        }

        if (running.Count > 0 && (DateTime.Now - _lastRefresh).TotalSeconds >= 30)
            RefreshData();
    }

    private void ShowToast(string message, ToastKind kind)
    {
        if (Window.GetWindow(this) is MainWindow main) main.ShowToast(message, kind);
    }

    private sealed class AppRowVm
    {
        public AppRowVm(WatchedApp app, DayAppStatus? status, bool running)
        {
            Id = app.Id;
            Name = app.Name;
            PathText = string.IsNullOrWhiteSpace(app.ExecutablePath)
                ? "路径受保护 · 按进程名匹配"
                : app.ExecutablePath;
            Enabled = app.Enabled;
            Shown = app.ShowInToday;
            Icon = IconService.GetIcon(app);
            Initial = ColorUtil.Initial(app.Name);
            BadgeBackground = ColorUtil.BlendWithWhite(app.Color, 0.86);
            BadgeForeground = ColorUtil.Brush(app.Color);
            TodayDuration = status is not null && status.TodaySeconds > 0 ? Fmt.Duration(status.TodaySeconds) : "—";
            TodayLaunches = status is not null && status.Launches > 0 ? $"{status.Launches} 次" : "—";
            DisabledBadgeVisibility = app.Enabled ? Visibility.Collapsed : Visibility.Visible;
            HiddenBadgeVisibility = app.ShowInToday ? Visibility.Collapsed : Visibility.Visible;

            if (!app.Enabled)
            {
                StatusText = "已暂停";
                StatusBackground = ColorUtil.Brush("#F3F3F7");
                StatusForeground = ColorUtil.Brush("#8A8C9C");
                StatusDot = ColorUtil.Brush("#B9BBC7");
            }
            else if (running)
            {
                StatusText = "运行中";
                StatusBackground = ColorUtil.Brush("#E9F9F2");
                StatusForeground = ColorUtil.Brush("#0C9B6B");
                StatusDot = ColorUtil.Brush("#13B981");
            }
            else
            {
                StatusText = "未运行";
                StatusBackground = ColorUtil.Brush("#F5F5F9");
                StatusForeground = ColorUtil.Brush("#7C7E90");
                StatusDot = ColorUtil.Brush("#B9BBC7");
            }
        }

        public string Id { get; }
        public string Name { get; }
        public string PathText { get; }
        public bool Enabled { get; }
        public bool Shown { get; }
        public ImageSource? Icon { get; }
        public string Initial { get; }
        public Brush BadgeBackground { get; }
        public Brush BadgeForeground { get; }
        public string TodayDuration { get; }
        public string TodayLaunches { get; }
        public string StatusText { get; }
        public Brush StatusBackground { get; }
        public Brush StatusForeground { get; }
        public Brush StatusDot { get; }
        public Visibility DisabledBadgeVisibility { get; }
        public Visibility HiddenBadgeVisibility { get; }
    }
}
