using ShiJi.Models;

namespace ShiJi.Services;

public enum StatsPeriod
{
    Day,
    Week,
    Month,
    Year
}

public sealed class PeriodWindow
{
    public StatsPeriod Period { get; init; }
    public DateOnly Anchor { get; init; }
    public DateOnly FromDay { get; init; }
    public DateOnly ToDay { get; init; }
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public string Label { get; init; } = "";
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public bool IsCurrent { get; init; }
    public int BucketCount { get; init; }
    public Func<int, DateTimeOffset> BucketStart { get; init; } = _ => default;
    public Func<int, string> BucketLabel { get; init; } = _ => "";
    public Func<int, string> BucketTooltip { get; init; } = _ => "";
}

public static class Periods
{
    public static PeriodWindow Build(StatsPeriod period, DateOnly anchor)
    {
        var today = TimeUtil.Today;
        switch (period)
        {
            case StatsPeriod.Week:
            {
                var start = TimeUtil.WeekStart(anchor);
                var end = start.AddDays(6);
                return new PeriodWindow
                {
                    Period = period,
                    Anchor = start,
                    FromDay = start,
                    ToDay = end,
                    From = TimeUtil.LocalDayStart(start),
                    To = TimeUtil.LocalDayEnd(end),
                    Label = IsBetween(today, start, end)
                        ? $"本周  {start.Month}月{start.Day}日 – {end.Month}月{end.Day}日"
                        : $"{start.Month}月{start.Day}日 – {end.Month}月{end.Day}日",
                    Title = "每日运行分布",
                    Subtitle = "周一至周日",
                    IsCurrent = IsBetween(today, start, end),
                    BucketCount = 7,
                    BucketStart = i => TimeUtil.LocalDayStart(start.AddDays(i)),
                    BucketLabel = i => "周" + "一二三四五六日"[i],
                    BucketTooltip = i =>
                    {
                        var day = start.AddDays(i);
                        return $"{day.Year}年{day.Month}月{day.Day}日 周{Fmt.Weekday(day)}";
                    }
                };
            }
            case StatsPeriod.Month:
            {
                var start = TimeUtil.MonthStart(anchor);
                var end = start.AddMonths(1).AddDays(-1);
                var days = DateTime.DaysInMonth(start.Year, start.Month);
                return new PeriodWindow
                {
                    Period = period,
                    Anchor = start,
                    FromDay = start,
                    ToDay = end,
                    From = TimeUtil.LocalDayStart(start),
                    To = TimeUtil.LocalDayEnd(end),
                    Label = IsBetween(today, start, end)
                        ? $"{start.Year}年{start.Month}月  ·  本月"
                        : $"{start.Year}年{start.Month}月",
                    Title = "每日运行分布",
                    Subtitle = $"{start.Month}月 1–{days} 日",
                    IsCurrent = IsBetween(today, start, end),
                    BucketCount = days,
                    BucketStart = i => TimeUtil.LocalDayStart(start.AddDays(i)),
                    BucketLabel = i => (i + 1).ToString(),
                    BucketTooltip = i =>
                        $"{start.Month}月{i + 1}日 周{Fmt.Weekday(start.AddDays(i))}"
                };
            }
            case StatsPeriod.Year:
            {
                var start = new DateOnly(anchor.Year, 1, 1);
                var end = new DateOnly(anchor.Year, 12, 31);
                return new PeriodWindow
                {
                    Period = period,
                    Anchor = start,
                    FromDay = start,
                    ToDay = end,
                    From = TimeUtil.LocalDayStart(start),
                    To = TimeUtil.LocalDayEnd(end),
                    Label = anchor.Year == today.Year ? $"{anchor.Year}年  ·  今年" : $"{anchor.Year}年",
                    Title = "每月运行分布",
                    Subtitle = "1–12 月",
                    IsCurrent = IsBetween(today, start, end),
                    BucketCount = 12,
                    BucketStart = i => TimeUtil.LocalDayStart(new DateOnly(anchor.Year, i + 1, 1)),
                    BucketLabel = i => $"{i + 1}月",
                    BucketTooltip = i => $"{anchor.Year}年{i + 1}月"
                };
            }
            default:
            {
                var start = anchor;
                var startTime = TimeUtil.LocalDayStart(start);
                var dayStartHour = TimeUtil.DayStartHour;
                return new PeriodWindow
                {
                    Period = period,
                    Anchor = start,
                    FromDay = start,
                    ToDay = start,
                    From = startTime,
                    To = TimeUtil.LocalDayEnd(start),
                    Label = $"{start.Month}月{start.Day}日 周{Fmt.Weekday(start)}",
                    Title = "每小时运行分布",
                    Subtitle = dayStartHour == 0
                        ? "00:00 – 24:00"
                        : $"{dayStartHour:00}:00 – 次日 {dayStartHour:00}:00",
                    IsCurrent = start == today,
                    BucketCount = 24,
                    BucketStart = i => startTime.AddHours(i),
                    BucketLabel = i => startTime.AddHours(i).Hour.ToString(),
                    BucketTooltip = i =>
                    {
                        var begin = startTime.AddHours(i);
                        var end = begin.AddHours(1);
                        return $"{begin:HH:00} – {end:HH:00}";
                    }
                };
            }
        }
    }

    public static DateOnly Shift(StatsPeriod period, DateOnly anchor, int delta)
    {
        return period switch
        {
            StatsPeriod.Week => anchor.AddDays(7 * delta),
            StatsPeriod.Month => anchor.AddMonths(delta),
            StatsPeriod.Year => anchor.AddYears(delta),
            _ => anchor.AddDays(delta)
        };
    }

    public static string Key(StatsPeriod period) => period switch
    {
        StatsPeriod.Week => "week",
        StatsPeriod.Month => "month",
        StatsPeriod.Year => "year",
        _ => "day"
    };

    public static StatsPeriod Parse(string? tag) => tag switch
    {
        "week" => StatsPeriod.Week,
        "month" => StatsPeriod.Month,
        "year" => StatsPeriod.Year,
        _ => StatsPeriod.Day
    };

    private static bool IsBetween(DateOnly day, DateOnly start, DateOnly end) =>
        day >= start && day <= end;
}
