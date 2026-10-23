using Microsoft.Win32;

namespace ShiJi.Services;

public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ShiJiProcessTimer";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) is string s && !string.IsNullOrWhiteSpace(s);
        }
        catch
        {
            return false;
        }
    }

    public static bool SetEnabled(bool enabled, out string? error)
    {
        error = null;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null)
            {
                error = "无法打开当前用户启动项。";
                return false;
            }

            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(exe))
                {
                    error = "无法获取程序路径。";
                    return false;
                }
                key.SetValue(ValueName, $"\"{exe}\" --minimized", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
