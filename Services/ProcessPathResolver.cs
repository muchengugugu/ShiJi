using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ShiJi.Services;

/// <summary>
/// Reads a process image path with the least privileges possible. Games and
/// anti-cheat protected processes often deny Process.MainModule but still allow
/// QueryFullProcessImageName with PROCESS_QUERY_LIMITED_INFORMATION.
/// </summary>
public static class ProcessPathResolver
{
    private const uint ProcessQueryLimitedInformation = 0x1000;

    public static string? TryGetPath(Process process)
    {
        try
        {
            var main = process.MainModule?.FileName;
            var normalized = Normalize(main);
            if (normalized is not null) return normalized;
        }
        catch
        {
            // Fall through to the limited-information query.
        }

        var handle = IntPtr.Zero;
        try
        {
            handle = OpenProcess(ProcessQueryLimitedInformation, false, process.Id);
            if (handle == IntPtr.Zero) return null;

            var buffer = new StringBuilder(2048);
            uint length = (uint)buffer.Capacity;
            if (!QueryFullProcessImageName(handle, 0, buffer, ref length)) return null;
            return Normalize(buffer.ToString(0, (int)length));
        }
        catch
        {
            return null;
        }
        finally
        {
            if (handle != IntPtr.Zero) CloseHandle(handle);
        }
    }

    public static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var result = Path.GetFullPath(path.Trim().Trim('"'));
            if (result.StartsWith(@"\\?\", StringComparison.Ordinal))
                result = result[4..];
            return result;
        }
        catch
        {
            return null;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(
        IntPtr processHandle, uint flags, StringBuilder exeName, ref uint size);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
