using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ShiJi.Services;
using ShiJi.Views;
using WinForms = System.Windows.Forms;

namespace ShiJi;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _toastTimer;
    private readonly Dictionary<string, UserControl> _pages = new(StringComparer.OrdinalIgnoreCase);
    private IRefreshablePage? _current;
    private string _currentKey = "overview";
    private WinForms.NotifyIcon? _tray;
    private int _tickCount;
    private bool _ready;
    private bool _syncingNav;
    private bool _updateCheckStarted;
    private string _updateReleaseUrl = "";
    private string _updateLatestVersion = "";

    public MainWindow()
    {
        InitializeComponent();

        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(Width, Math.Max(MinWidth, workArea.Width - 24));
        Height = Math.Min(Height, Math.Max(MinHeight, workArea.Height - 24));

        _clockTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, Clock_Tick, Dispatcher);
        _toastTimer = new DispatcherTimer(TimeSpan.FromSeconds(3.2), DispatcherPriority.Background, Toast_Tick, Dispatcher);
        _toastTimer.Stop();

        InitTray();
        App.Store.Changed += Store_Changed;
        Loaded += MainWindow_Loaded;

        var last = App.Store.GetSettings().LastView;
        var radio = last switch
        {
            "records" => NavRecords,
            "stats" => NavStats,
            "calendar" => NavCalendar,
            "watchlist" => NavWatchlist,
            "settings" => NavSettings,
            _ => NavOverview
        };
        radio.IsChecked = true;
        _ready = true;
        NavigateTo((string)radio.Tag, persist: false);
        UpdateStatus();
        _clockTimer.Start();
    }

    public string CurrentPageKey => _currentKey;
    public bool TrayAvailable => _tray is not null;

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_updateCheckStarted) return;
        _updateCheckStarted = true;
        try
        {
            await Task.Delay(4000);
            var result = await UpdateService.CheckAsync(force: false);
            if (!IsLoaded) return;
            if (result.UpdateAvailable)
            {
                ShowUpdateBanner(result, force: false);
            }
            _current?.RefreshData();
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    public void ShowUpdateBanner(UpdateCheckResult result, bool force)
    {
        if (!result.UpdateAvailable) return;
        if (!force && UpdateService.IsDismissed(result.LatestVersion)) return;

        _updateLatestVersion = result.LatestVersion;
        _updateReleaseUrl = result.ReleaseUrl;
        UpdateBannerText.Text = "发现新版本 " + result.LatestVersion;
        UpdateBanner.Visibility = Visibility.Visible;
    }

    private void UpdateBannerView_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_updateReleaseUrl))
        {
            ShowToast("没有可用的更新地址。", ToastKind.Warning);
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _updateReleaseUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowToast("打开更新页面失败：" + ex.Message, ToastKind.Error);
        }
    }

    private void UpdateBannerClose_Click(object sender, RoutedEventArgs e)
    {
        UpdateBanner.Visibility = Visibility.Collapsed;
        UpdateService.Dismiss(_updateLatestVersion);
    }

    public WatchlistView OpenWatchlist()
    {
        NavigateTo("watchlist");
        return GetPage<WatchlistView>("watchlist");
    }

    public void RefreshAllPages()
    {
        foreach (var page in _pages.Values)
        {
            if (page is IRefreshablePage refreshable)
            {
                try { refreshable.RefreshData(); } catch (Exception ex) { App.Log(ex); }
            }
        }
    }

    private T GetPage<T>(string key) where T : UserControl, new()
    {
        if (_pages.TryGetValue(key, out var existing) && existing is T typed) return typed;
        var page = new T();
        _pages[key] = page;
        return page;
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready || _syncingNav || sender is not RadioButton radio || radio.Tag is not string key) return;
        NavigateTo(key, persist: true);
    }

    public void NavigateTo(string key, bool persist = true)
    {
        key = key.ToLowerInvariant();
        UserControl page = key switch
        {
            "records" => GetPage<DetailsView>("records"),
            "stats" => GetPage<StatsView>("stats"),
            "calendar" => GetPage<CalendarView>("calendar"),
            "watchlist" => GetPage<WatchlistView>("watchlist"),
            "settings" => GetPage<SettingsView>("settings"),
            _ => GetPage<OverviewView>("overview")
        };

        key = key switch
        {
            "records" or "stats" or "calendar" or "watchlist" or "settings" => key,
            _ => "overview"
        };

        if (page is DetailsView details)
        {
            details.AppSelected -= Details_AppSelected;
            details.AppSelected += Details_AppSelected;
        }

        _currentKey = key;
        PageHost.Content = page;
        _current = page as IRefreshablePage;
        _current?.RefreshData();

        var radio = key switch
        {
            "records" => NavRecords,
            "stats" => NavStats,
            "calendar" => NavCalendar,
            "watchlist" => NavWatchlist,
            "settings" => NavSettings,
            _ => NavOverview
        };
        if (!radio.IsChecked.GetValueOrDefault())
        {
            _syncingNav = true;
            radio.IsChecked = true;
            _syncingNav = false;
        }

        if (persist)
        {
            try { App.Store.UpdateSettings(s => s.LastView = key); } catch (Exception ex) { App.Log(ex); }
        }
    }

    private void Details_AppSelected(string appId, StatsPeriod period, DateOnly anchor)
    {
        var page = GetPage<AppDetailView>("appdetail");
        page.BackRequested -= AppDetail_BackRequested;
        page.BackRequested += AppDetail_BackRequested;
        page.LoadApp(appId, period, anchor);
        _currentKey = "appdetail";
        PageHost.Content = page;
        _current = page;

        if (NavRecords.IsChecked != true)
        {
            _syncingNav = true;
            NavRecords.IsChecked = true;
            _syncingNav = false;
        }
    }

    private void AppDetail_BackRequested()
    {
        NavigateTo("records");
    }

    private void Clock_Tick(object? sender, EventArgs e)
    {
        _tickCount++;
        UpdateStatus();
        _current?.OnTick();
        FooterRefreshText.Text = DateTime.Now.ToString("HH:mm:ss") + " 更新";
    }

    private void UpdateStatus()
    {
        var running = App.Tracker.RunningAppCount;
        SidebarRunningText.Text = running > 0 ? $"{running} 个应用正在运行" : "当前没有程序运行";
    }

    private void Store_Changed(object? sender, EventArgs e)
    {
        if (App.IsExiting) return;
        try
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (!_ready || App.IsExiting) return;
                _current?.RefreshData();
            }));
        }
        catch (InvalidOperationException)
        {
            // The dispatcher has already begun shutting down.
        }
    }

    public void ShowToast(string message, ToastKind kind = ToastKind.Info)
    {
        ToastText.Text = message;
        ToastGlyph.Text = kind switch
        {
            ToastKind.Success => "\uE73E",
            ToastKind.Warning => "\uE7BA",
            ToastKind.Error => "\uE783",
            _ => "\uE946"
        };
        ToastBorder.Visibility = Visibility.Visible;
        ToastBorder.Opacity = 0;
        ToastTranslate.Y = 8;
        ToastBorder.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        ToastTranslate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void Toast_Tick(object? sender, EventArgs e)
    {
        _toastTimer.Stop();
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) => ToastBorder.Visibility = Visibility.Collapsed;
        ToastBorder.BeginAnimation(OpacityProperty, fade);
    }

    public void HideToTray(bool silent)
    {
        if (_tray is null)
        {
            Show();
            return;
        }
        Hide();
        ShowInTaskbar = false;
        if (silent) return;
        var settings = App.Store.GetSettings();
        if (settings.FirstTrayHintShown) return;
        try
        {
            _tray?.ShowBalloonTip(2600, "时迹仍在记录",
                "窗口已收进系统托盘，进程计时不会中断。双击托盘图标可重新打开。",
                WinForms.ToolTipIcon.Info);
            App.Store.UpdateSettings(s => s.FirstTrayHintShown = true);
        }
        catch { }
    }

    public void ShowFromTray()
    {
        ShowInTaskbar = true;
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void InitTray()
    {
        try
        {
            var icon = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/Assets/app.ico"))?.Stream;
            var trayIcon = icon is null
                ? System.Drawing.SystemIcons.Application
                : new System.Drawing.Icon(icon);
            _tray = new WinForms.NotifyIcon
            {
                Icon = trayIcon,
                Text = "时迹 · 进程时长记录",
                Visible = true
            };
            var menu = new WinForms.ContextMenuStrip();
            menu.Items.Add("打开时迹", null, (_, _) => ShowFromTray());
            menu.Items.Add("查看今日概览", null, (_, _) =>
            {
                ShowFromTray();
                NavigateTo("overview");
            });
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add("退出时迹", null, (_, _) => ExitApp());
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += (_, _) => ShowFromTray();
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    public void ExitApp()
    {
        if (App.IsExiting) return;
        App.IsExiting = true;
        _clockTimer.Stop();
        try { App.Tracker.Stop(); } catch (Exception ex) { App.Log(ex); }
        try
        {
            if (_tray is not null)
            {
                _tray.Visible = false;
                _tray.Dispose();
                _tray = null;
            }
        }
        catch { }
        System.Windows.Application.Current.Shutdown();
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (App.IsExiting) return;
        if (e is null) return;

        if (System.Windows.Application.Current.ShutdownMode == ShutdownMode.OnExplicitShutdown)
        {
            var settings = App.Store.GetSettings();
            if (settings.MinimizeToTray && _tray is not null)
            {
                e.Cancel = true;
                HideToTray(false);
                return;
            }

            e.Cancel = true;
            ExitApp();
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _clockTimer.Stop();
        _toastTimer.Stop();
        try
        {
            if (_tray is not null)
            {
                _tray.Visible = false;
                _tray.Dispose();
                _tray = null;
            }
        }
        catch { }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.F5)
        {
            _current?.RefreshData();
            UpdateStatus();
            e.Handled = true;
        }
    }
}
