using System.Text;
using ShiJi.Models;

namespace ShiJi.Services;

public static class CsvExporter
{
    public static void ExportSessions(string path, IEnumerable<SessionRecord> sessions, IReadOnlyList<WatchedApp> apps)
    {
        var appMap = apps.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);
        var now = DateTimeOffset.UtcNow;
        var sb = new StringBuilder();
        sb.AppendLine("应用,程序路径,开始时间,结束时间,时长秒,时长,状态,来源");
        foreach (var session in sessions.OrderBy(s => s.Start))
        {
            var app = appMap.GetValueOrDefault(session.AppId);
            sb.Append(Escape(app?.Name ?? session.AppName)).Append(',');
            sb.Append(Escape(app?.ExecutablePath ?? "")).Append(',');
            sb.Append(Escape(session.Start.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"))).Append(',');
            sb.Append(Escape(session.End?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "运行中")).Append(',');
            sb.Append((long)session.DurationAt(now).TotalSeconds).Append(',');
            sb.Append(Escape(Fmt.DurationFull(session.DurationAt(now).TotalSeconds))).Append(',');
            sb.Append(Escape(session.IsOpen ? "运行中" : "已结束")).Append(',');
            sb.AppendLine(Escape(session.IsImported ? "Tai 导入" : "本机记录"));
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string Escape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}
