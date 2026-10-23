using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using ShiJi.Services;
using ShiJi.Views;

namespace ShiJi;

public partial class App : System.Windows.Application
{
    private Mutex? _mutex;
    public static DataStore Store { get; private set; } = null!;
    public static TrackerService Tracker { get; private set; } = null!;
    public static MainWindow? MainWindowInstance { get; private set; }
    public static bool IsExiting { get; set; }
    public static string LogFile => Path.Combine(Store.BaseDirectory, "error.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var allowMultiple = e.Args.Any(a => string.Equals(a, "--allow-multiple", StringComparison.OrdinalIgnoreCase)) ||
                            Environment.GetEnvironmentVariable("SHIJI_ALLOW_MULTIPLE") == "1";
        if (!allowMultiple)
        {
            _mutex = new Mutex(true, "ShiJi.ProcessTimer.SingleInstance", out var createdNew);
            if (!createdNew)
            {
                System.Windows.MessageBox.Show(
                    "时迹已经在运行中。\n\n可以在系统托盘双击时钟图标重新打开窗口。",
                    "时迹", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log(args.ExceptionObject as Exception);

        try
        {
            Store = new DataStore();
            Store.Load();
            Fmt.MaxUnitIsHours = string.Equals(Store.GetSettings().DurationMode, "hours", StringComparison.OrdinalIgnoreCase);
            TimeUtil.DayStartHour = Store.GetSettings().DayStartHour == 4 ? 4 : 0;
        }
        catch (Exception ex)
        {
            Log(ex);
            System.Windows.MessageBox.Show(
                "时迹启动失败：\n" + ex.Message,
                "时迹", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        try
        {
            Tracker = new TrackerService(Store);
            Tracker.Start();
        }
        catch (Exception ex)
        {
            Log(ex);
            System.Windows.MessageBox.Show(
                "时迹启动失败：\n" + ex.Message,
                "时迹", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        var window = new MainWindow();
        MainWindowInstance = window;
        MainWindow = window;
        var startHidden = e.Args.Any(a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase));
        window.Show();
        if (startHidden && window.TrayAvailable) window.HideToTray(silent: true);
        else window.Activate();

        if (Store.LoadWarning is not null)
        {
            Dispatcher.BeginInvoke(() => window.ShowToast(Store.LoadWarning, ToastKind.Warning),
                DispatcherPriority.Background);
        }
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        IsExiting = true;
        try { Tracker.Stop(); } catch (Exception ex) { Log(ex); }
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        IsExiting = true;
        try
        {
            Tracker?.Stop();
            Tracker?.Dispose();
        }
        catch (Exception ex) { Log(ex); }

        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log(e.Exception);
        e.Handled = true;
        System.Windows.MessageBox.Show(
            "发生了一个错误：\n" + e.Exception.Message +
            "\n\n详细信息已写入：\n" + LogFile,
            "时迹", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    public static void Log(Exception? ex)
    {
        if (ex is null) return;
        try
        {
            Directory.CreateDirectory(Store?.BaseDirectory ?? DataStore.ResolveDefaultDirectory());
            File.AppendAllText(
                LogFile,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch { }
    }
}
