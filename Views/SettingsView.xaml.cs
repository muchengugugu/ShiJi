using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ShiJi.Services;

namespace ShiJi.Views;

public partial class SettingsView : UserControl, IRefreshablePage
{
    private bool _ready;
    private bool _suppress;
    private bool _checkingUpdate;

    public SettingsView()
    {
        InitializeComponent();
        _ready = true;
        VersionText.Text = "时迹 " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0");
    }

    public void RefreshData()
    {
        if (!_ready) return;
        _suppress = true;
        try
        {
            var settings = App.Store.GetSettings();
            StartupToggle.IsChecked = StartupManager.IsEnabled();
            TrayToggle.IsChecked = settings.MinimizeToTray;
            var hoursMode = string.Equals(settings.DurationMode, "hours", StringComparison.OrdinalIgnoreCase);
            DurationAuto.IsChecked = !hoursMode;
            DurationHours.IsChecked = hoursMode;
            var four = settings.DayStartHour == 4;
            DayStartZero.IsChecked = !four;
            DayStartFour.IsChecked = four;
        }
        finally
        {
            _suppress = false;
        }

        DataPathText.Text = App.Store.DataFile;
        ApplyUpdateStatus(UpdateService.CachedResult());
    }

    private void ApplyUpdateStatus(UpdateCheckResult result)
    {
        if (result.UpdateAvailable)
        {
            UpdateStatusText.Text = "发现新版本 " + result.LatestVersion;
        }
        else if (!string.IsNullOrWhiteSpace(result.Error))
        {
            UpdateStatusText.Text = "检查更新失败";
        }
        else if (!string.IsNullOrWhiteSpace(result.LatestVersion))
        {
            UpdateStatusText.Text = "已是最新版本";
        }
        else
        {
            UpdateStatusText.Text = "尚未检查更新";
        }
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_checkingUpdate) return;
        _checkingUpdate = true;
        CheckUpdateButton.IsEnabled = false;
        UpdateStatusText.Text = "正在检查更新…";
        try
        {
            var result = await UpdateService.CheckAsync(force: true);
            ApplyUpdateStatus(result);

            if (result.UpdateAvailable)
            {
                App.MainWindowInstance?.ShowUpdateBanner(result, force: true);
                ShowToast("发现新版本 " + result.LatestVersion + "。", ToastKind.Success);
            }
            else if (!string.IsNullOrWhiteSpace(result.Error))
            {
                ShowToast("检查更新失败：" + result.Error, ToastKind.Error);
            }
            else
            {
                ShowToast("已是最新版本。", ToastKind.Success);
            }
        }
        catch (Exception ex)
        {
            App.Log(ex);
            UpdateStatusText.Text = "检查更新失败";
            ShowToast("检查更新失败：" + ex.Message, ToastKind.Error);
        }
        finally
        {
            _checkingUpdate = false;
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private void StartupToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready || _suppress) return;
        var enabled = StartupToggle.IsChecked == true;
        if (!StartupManager.SetEnabled(enabled, out var error))
        {
            _suppress = true;
            StartupToggle.IsChecked = !enabled;
            _suppress = false;
            ShowToast(error ?? "设置开机启动失败。", ToastKind.Error);
            return;
        }

        App.Store.UpdateSettings(s => s.LaunchAtStartup = enabled);
        ShowToast(enabled ? "已设置为开机自动启动。" : "已关闭开机自动启动。", ToastKind.Success);
    }

    private void TrayToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready || _suppress) return;
        var enabled = TrayToggle.IsChecked == true;
        App.Store.UpdateSettings(s => s.MinimizeToTray = enabled);
        ShowToast(enabled
            ? "关闭窗口后会继续在托盘记录。"
            : "关闭窗口将直接退出，计时会停止。", enabled ? ToastKind.Success : ToastKind.Warning);
    }

    private void DurationMode_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready || _suppress) return;
        var hoursMode = DurationHours.IsChecked == true;
        Fmt.MaxUnitIsHours = hoursMode;
        App.Store.UpdateSettings(s => s.DurationMode = hoursMode ? "hours" : "auto");
        ShowToast(hoursMode
            ? "时长改为小时单位。"
            : "时长改为自动（天 / 小时）。", ToastKind.Success);
    }

    private void DayStart_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready || _suppress) return;
        var hour = DayStartFour.IsChecked == true ? 4 : 0;
        TimeUtil.DayStartHour = hour;
        App.Store.UpdateSettings(s => s.DayStartHour = hour);
        if (Window.GetWindow(this) is MainWindow main) main.RefreshAllPages();
        ShowToast(hour == 4 ? "已设置为每天四点更新。" : "已设置为每天零点更新。", ToastKind.Success);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{App.Store.BaseDirectory}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowToast("打开文件夹失败：" + ex.Message, ToastKind.Error);
        }
    }

    private void ExportAll_Click(object sender, RoutedEventArgs e)
    {
        var sessions = App.Store.GetSessions();
        if (sessions.Count == 0)
        {
            ShowToast("还没有可导出的记录。", ToastKind.Warning);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "CSV 文件|*.csv",
            FileName = $"时迹-全部记录-{DateTime.Now:yyyyMMdd}.csv",
            AddExtension = true,
            DefaultExt = ".csv"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            CsvExporter.ExportSessions(dialog.FileName, sessions, App.Store.GetApps());
            ShowToast($"已导出 {sessions.Count} 条记录。", ToastKind.Success);
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowToast("导出失败：" + ex.Message, ToastKind.Error);
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        var sessions = App.Store.GetSessions();
        var completed = sessions.Count(s => !s.IsOpen);
        if (completed == 0)
        {
            ShowToast("没有已结束的历史记录可以清空。", ToastKind.Warning);
            return;
        }

        var result = System.Windows.MessageBox.Show(
            $"将删除 {completed} 条已结束的历史记录。\n\n应用名单不会改变，正在运行的应用当前记录会保留。\n此操作无法撤销。",
            "清空统计", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        var removed = App.Store.ClearCompletedSessions();
        RefreshData();
        ShowToast($"已清空 {removed} 条历史记录。", ToastKind.Success);
    }

    private void OpenGitHub_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/muchengugugu/ShiJi",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowToast("打开 GitHub 失败：" + ex.Message, ToastKind.Error);
        }
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!File.Exists(App.LogFile))
            {
                ShowToast("暂时没有错误日志。", ToastKind.Info);
                return;
            }
            System.Diagnostics.Process.Start(new ProcessStartInfo
            {
                FileName = "notepad.exe",
                Arguments = $"\"{App.LogFile}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowToast("打开日志失败：" + ex.Message, ToastKind.Error);
        }
    }

    public void OnTick()
    {
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return bytes + " B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
        return $"{bytes / 1024.0 / 1024.0:0.#} MB";
    }

    private void ShowToast(string message, ToastKind kind)
    {
        if (Window.GetWindow(this) is MainWindow main) main.ShowToast(message, kind);
    }
}
