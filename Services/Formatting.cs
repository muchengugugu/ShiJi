namespace ShiJi.Services;

public static class Fmt
{
    /// <summary>When true, durations never use days; the largest unit is hours.</summary>
    public static bool MaxUnitIsHours { get; set; }

    public static string Duration(double seconds)
    {
        var total = (long)Math.Floor(Math.Max(0, seconds));
        if (total <= 0) return "0秒";
        if (MaxUnitIsHours) return HoursDuration(total);

        var days = total / 86400;
        var hours = total % 86400 / 3600;
        var minutes = total % 3600 / 60;
        var secs = total % 60;

        if (days > 0)
            return hours > 0 ? $"{days}天 {hours}小时" : $"{days}天";
        if (hours > 0)
            return minutes > 0 ? $"{hours}小时{minutes}分" : $"{hours}小时";
        if (minutes > 0)
            return secs > 0 ? $"{minutes}分{secs}秒" : $"{minutes}分钟";
        return $"{secs}秒";
    }

    private static string HoursDuration(long total)
    {
        var hours = total / 3600;
        var minutes = total % 3600 / 60;
        var secs = total % 60;
        if (hours > 0)
            return minutes > 0 ? $"{hours}小时{minutes}分" : $"{hours}小时";
        if (minutes > 0)
            return secs > 0 ? $"{minutes}分{secs}秒" : $"{minutes}分钟";
        return $"{secs}秒";
    }

    public static string Duration(TimeSpan span) => Duration(span.TotalSeconds);

    public static string DurationFull(double seconds)
    {
        var total = (long)Math.Floor(Math.Max(0, seconds));
        if (total <= 0) return "0秒";
        var days = total / 86400;
        var hours = total % 86400 / 3600;
        var minutes = total % 3600 / 60;
        var secs = total % 60;
        if (days > 0) return $"{days}天 {hours}小时 {minutes}分";
        if (hours > 0) return $"{hours}小时 {minutes}分 {secs}秒";
        if (minutes > 0) return $"{minutes}分 {secs}秒";
        return $"{secs}秒";
    }

    public static string DurationShort(double seconds)
    {
        var total = (long)Math.Floor(Math.Max(0, seconds));
        if (total <= 0) return "—";
        if (total < 60) return $"{total}秒";
        if (total < 3600)
        {
            var minutes = total / 60;
            return minutes < 10 ? $"{minutes}分" : $"{minutes}分";
        }
        var hours = total / 3600.0;
        if (hours < 10) return $"{hours:0.#}小时";
        return $"{total / 3600}小时";
    }

    public static string Clock(DateTimeOffset? time)
    {
        if (time is null) return "—";
        return time.Value.ToLocalTime().ToString("HH:mm:ss");
    }

    public static string Date(DateTimeOffset time) =>
        time.ToLocalTime().ToString("yyyy年M月d日");

    public static string DateTime(DateTimeOffset time) =>
        time.ToLocalTime().ToString("yyyy年M月d日 HH:mm:ss");

    public static string ShortDate(DateTimeOffset time) =>
        time.ToLocalTime().ToString("M月d日");

    public static string DayTitle(DateOnly day)
    {
        var today = TimeUtil.Today;
        if (day == today) return "今天";
        if (day == today.AddDays(-1)) return "昨天";
        if (day == today.AddDays(1)) return "明天";
        return $"{day.Month}月{day.Day}日 周{Weekday(day)}";
    }

    public static string ShortDay(DateOnly day)
    {
        var today = TimeUtil.Today;
        if (day == today) return "今天";
        if (day == today.AddDays(-1)) return "昨天";
        return $"{day.Month}月{day.Day}日";
    }

    public static string DayTitleWithYear(DateOnly day) =>
        $"{day.Year}年{day.Month}月{day.Day}日 周{Weekday(day)}";

    public static string Weekday(DateOnly day) => day.DayOfWeek switch
    {
        DayOfWeek.Monday => "一",
        DayOfWeek.Tuesday => "二",
        DayOfWeek.Wednesday => "三",
        DayOfWeek.Thursday => "四",
        DayOfWeek.Friday => "五",
        DayOfWeek.Saturday => "六",
        _ => "日"
    };

    public static string Timer(DateTimeOffset secondsNow, DateTimeOffset now)
    {
        _ = secondsNow;
        _ = now;
        return "";
    }

    public static string Ago(DateTimeOffset time, DateTimeOffset now)
    {
        var span = now - time;
        if (span.TotalSeconds < 5) return "刚刚";
        if (span.TotalMinutes < 1) return $"{(int)span.TotalSeconds} 秒前";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes} 分钟前";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours} 小时前";
        return $"{(int)span.TotalDays} 天前";
    }
}

public static class TimeUtil
{
    /// <summary>Hour at which a logical day starts (0 or 4).</summary>
    public static int DayStartHour { get; set; }

    public static DateOnly Today
    {
        get
        {
            var now = DateTime.Now;
            var date = DateOnly.FromDateTime(now);
            return now.TimeOfDay < TimeSpan.FromHours(DayStartHour)
                ? date.AddDays(-1)
                : date;
        }
    }

    public static DateOnly LocalDate(DateTimeOffset instant)
    {
        var local = instant.ToLocalTime();
        var date = DateOnly.FromDateTime(local.DateTime);
        return local.TimeOfDay < TimeSpan.FromHours(DayStartHour)
            ? date.AddDays(-1)
            : date;
    }

    public static DateTimeOffset LocalDayStart(DateOnly day)
    {
        var local = day.ToDateTime(
            TimeOnly.FromTimeSpan(TimeSpan.FromHours(DayStartHour)),
            DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    public static DateTimeOffset LocalDayEnd(DateOnly day) => LocalDayStart(day.AddDays(1));

    public static DateOnly WeekStart(DateOnly day) =>
        day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    public static DateOnly MonthStart(DateOnly day) => new(day.Year, day.Month, 1);

    public static int DaysInMonth(DateOnly day) =>
        DateTime.DaysInMonth(day.Year, day.Month);

    public static double OverlapSeconds(
        DateTimeOffset start,
        DateTimeOffset? end,
        DateTimeOffset from,
        DateTimeOffset to,
        DateTimeOffset now)
    {
        var effectiveEnd = end ?? now;
        var s = start > from ? start : from;
        var e = effectiveEnd < to ? effectiveEnd : to;
        if (e <= s) return 0;
        return (e - s).TotalSeconds;
    }

    public static bool Overlaps(
        DateTimeOffset start,
        DateTimeOffset? end,
        DateTimeOffset from,
        DateTimeOffset to,
        DateTimeOffset now)
    {
        var effectiveEnd = end ?? now;
        return start < to && effectiveEnd > from;
    }

    public static IEnumerable<DateOnly> Days(DateOnly from, DateOnly to)
    {
        for (var d = from; d <= to; d = d.AddDays(1)) yield return d;
    }
}
