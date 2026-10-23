using ShiJi.Models;

namespace ShiJi.Services;

public sealed class PeriodSummary
{
    public double TotalSeconds { get; init; }
    public int LaunchCount { get; init; }
    public int AppCount { get; init; }
    public int RunningCount { get; init; }
    public string LongestAppName { get; init; } = "—";
    public double LongestSeconds { get; init; }
    public int SessionCount { get; init; }
}

public sealed class AppAggregate
{
    public string AppId { get; init; } = "";
    public string Name { get; init; } = "";
    public string Color { get; init; } = "#6C5CE7";
    public double Seconds { get; set; }
    public int Launches { get; set; }
    public int Sessions { get; set; }
    public bool Running { get; set; }
}

public sealed class BucketSlice
{
    public string AppId { get; init; } = "";
    public string Name { get; init; } = "";
    public string Color { get; init; } = "#6C5CE7";
    public double Seconds { get; set; }
}

public sealed class TimeBucket
{
    public DateTimeOffset Start { get; init; }
    public DateTimeOffset End { get; init; }
    public List<BucketSlice> Slices { get; } = new();
    public double Total => Slices.Sum(s => s.Seconds);
}

public sealed class DayAppStatus
{
    public WatchedApp App { get; init; } = new();
    public bool Launched { get; init; }
    public bool Running { get; init; }
    public int Launches { get; init; }
    public DateTimeOffset? FirstLaunch { get; init; }
    public DateTimeOffset? LastLaunch { get; init; }
    public double TodaySeconds { get; init; }
}

public sealed class AppDayCell
{
    public string AppId { get; init; } = "";
    public DateOnly Day { get; init; }
    public int Launches { get; init; }
    public double Seconds { get; init; }
    public DateTimeOffset? FirstLaunch { get; init; }
}

public sealed class DayTotal
{
    public DateOnly Day { get; init; }
    public double Seconds { get; init; }
    public int Launches { get; init; }
}

public static class StatsService
{
    public static DateOnly LocalDate(DateTimeOffset t) => TimeUtil.LocalDate(t);

    public static PeriodSummary Summarize(
        IReadOnlyList<SessionRecord> sessions,
        DateTimeOffset from,
        DateTimeOffset to,
        DateTimeOffset now,
        IReadOnlyList<WatchedApp>? apps = null,
        int runningCount = 0)
    {
        var appIds = apps?.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);
        double total = 0;
        var byApp = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var count = 0;
        var sessionsCount = 0;
        foreach (var session in sessions)
        {
            var overlap = TimeUtil.OverlapSeconds(session.Start, session.End, from, to, now);
            if (overlap <= 0) continue;
            total += overlap;
            sessionsCount++;
            if (session.Start >= from && session.Start < to) count++;
            byApp[session.AppId] = byApp.GetValueOrDefault(session.AppId) + overlap;
        }

        var longestId = byApp.OrderByDescending(x => x.Value).Select(x => x.Key).FirstOrDefault();
        var longestName = "—";
        if (longestId is not null)
        {
            longestName = appIds?.GetValueOrDefault(longestId)?.Name
                          ?? sessions.FirstOrDefault(s => s.AppId == longestId)?.AppName
                          ?? "—";
        }

        return new PeriodSummary
        {
            TotalSeconds = total,
            LaunchCount = count,
            AppCount = byApp.Count,
            RunningCount = runningCount,
            LongestAppName = longestName,
            LongestSeconds = longestId is null ? 0 : byApp[longestId],
            SessionCount = sessionsCount
        };
    }

    public static List<AppAggregate> AggregateApps(
        IReadOnlyList<SessionRecord> sessions,
        DateTimeOffset from,
        DateTimeOffset to,
        DateTimeOffset now,
        IReadOnlyList<WatchedApp>? apps = null,
        IReadOnlySet<string>? runningIds = null)
    {
        var appMap = apps?.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase)
                     ?? new Dictionary<string, WatchedApp>(StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, AppAggregate>(StringComparer.OrdinalIgnoreCase);

        foreach (var session in sessions)
        {
            var overlap = TimeUtil.OverlapSeconds(session.Start, session.End, from, to, now);
            var launched = session.Start >= from && session.Start < to;
            if (overlap <= 0 && !launched) continue;

            if (!result.TryGetValue(session.AppId, out var agg))
            {
                var current = appMap.GetValueOrDefault(session.AppId);
                agg = new AppAggregate
                {
                    AppId = session.AppId,
                    Name = current?.Name ?? session.AppName,
                    Color = current?.Color ?? session.AppColor
                };
                result[session.AppId] = agg;
            }

            agg.Seconds += overlap;
            agg.Sessions++;
            if (launched) agg.Launches++;
            if (runningIds is not null && runningIds.Contains(session.AppId)) agg.Running = true;
        }

        return result.Values
            .OrderByDescending(a => a.Seconds)
            .ThenByDescending(a => a.Launches)
            .ToList();
    }

    public static List<TimeBucket> BuildBuckets(
        IReadOnlyList<SessionRecord> sessions,
        DateTimeOffset from,
        DateTimeOffset to,
        int count,
        Func<int, DateTimeOffset> bucketStart,
        DateTimeOffset now,
        IReadOnlyList<WatchedApp>? apps = null)
    {
        var appMap = apps?.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase)
                     ?? new Dictionary<string, WatchedApp>(StringComparer.OrdinalIgnoreCase);
        var buckets = new List<TimeBucket>(count);
        for (var i = 0; i < count; i++)
        {
            var start = bucketStart(i);
            var end = i + 1 < count ? bucketStart(i + 1) : to;
            if (end < start) end = start;
            buckets.Add(new TimeBucket { Start = start, End = end });
        }

        foreach (var session in sessions)
        {
            if (!TimeUtil.Overlaps(session.Start, session.End, from, to, now)) continue;
            var current = appMap.GetValueOrDefault(session.AppId);
            for (var i = 0; i < buckets.Count; i++)
            {
                var bucket = buckets[i];
                var seconds = TimeUtil.OverlapSeconds(session.Start, session.End, bucket.Start, bucket.End, now);
                if (seconds <= 0) continue;
                var slice = bucket.Slices.FirstOrDefault(s => s.AppId == session.AppId);
                if (slice is null)
                {
                    slice = new BucketSlice
                    {
                        AppId = session.AppId,
                        Name = current?.Name ?? session.AppName,
                        Color = current?.Color ?? session.AppColor
                    };
                    bucket.Slices.Add(slice);
                }
                slice.Seconds += seconds;
            }
        }

        foreach (var bucket in buckets) bucket.Slices.Sort((a, b) => b.Seconds.CompareTo(a.Seconds));
        return buckets;
    }

    public static List<DayAppStatus> GetDayStatus(
        IReadOnlyList<WatchedApp> apps,
        IReadOnlyList<SessionRecord> sessions,
        DateOnly day,
        DateTimeOffset now,
        IReadOnlySet<string>? runningIds = null)
    {
        var from = TimeUtil.LocalDayStart(day);
        var to = TimeUtil.LocalDayEnd(day);
        var result = new List<DayAppStatus>(apps.Count);

        foreach (var app in apps)
        {
            var appSessions = sessions.Where(s => s.AppId == app.Id).ToList();
            var launchedToday = appSessions
                .Where(s => s.Start >= from && s.Start < to)
                .OrderBy(s => s.Start)
                .ToList();
            var seconds = appSessions.Sum(s => TimeUtil.OverlapSeconds(s.Start, s.End, from, to, now));

            result.Add(new DayAppStatus
            {
                App = app,
                Launched = launchedToday.Count > 0,
                Running = runningIds?.Contains(app.Id) == true,
                Launches = launchedToday.Count,
                FirstLaunch = launchedToday.FirstOrDefault()?.Start,
                LastLaunch = launchedToday.LastOrDefault()?.Start,
                TodaySeconds = seconds
            });
        }

        return result;
    }

    public static List<AppDayCell> GetMatrix(
        IReadOnlyList<WatchedApp> apps,
        IReadOnlyList<SessionRecord> sessions,
        DateOnly fromDay,
        DateOnly toDay,
        DateTimeOffset now)
    {
        var from = TimeUtil.LocalDayStart(fromDay);
        var to = TimeUtil.LocalDayEnd(toDay);
        var relevant = sessions
            .Where(s => TimeUtil.Overlaps(s.Start, s.End, from, to, now))
            .ToList();
        var byApp = relevant
            .GroupBy(s => s.AppId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var cells = new List<AppDayCell>();
        foreach (var app in apps)
        {
            var appSessions = byApp.GetValueOrDefault(app.Id) ?? new List<SessionRecord>();
            foreach (var day in TimeUtil.Days(fromDay, toDay))
            {
                var dayStart = TimeUtil.LocalDayStart(day);
                var dayEnd = TimeUtil.LocalDayEnd(day);
                var sessionsToday = appSessions
                    .Where(s => s.Start >= dayStart && s.Start < dayEnd)
                    .OrderBy(s => s.Start)
                    .ToList();
                var seconds = appSessions.Sum(s => TimeUtil.OverlapSeconds(s.Start, s.End, dayStart, dayEnd, now));
                cells.Add(new AppDayCell
                {
                    AppId = app.Id,
                    Day = day,
                    Launches = sessionsToday.Count,
                    Seconds = seconds,
                    FirstLaunch = sessionsToday.FirstOrDefault()?.Start
                });
            }
        }

        return cells;
    }

    public static List<DayTotal> GetDayTotals(
        IReadOnlyList<SessionRecord> sessions,
        DateOnly fromDay,
        DateOnly toDay,
        DateTimeOffset now)
    {
        var result = new List<DayTotal>();
        foreach (var day in TimeUtil.Days(fromDay, toDay))
        {
            var dayStart = TimeUtil.LocalDayStart(day);
            var dayEnd = TimeUtil.LocalDayEnd(day);
            double seconds = 0;
            var launches = 0;
            foreach (var session in sessions)
            {
                seconds += TimeUtil.OverlapSeconds(session.Start, session.End, dayStart, dayEnd, now);
                if (session.Start >= dayStart && session.Start < dayEnd) launches++;
            }
            result.Add(new DayTotal { Day = day, Seconds = seconds, Launches = launches });
        }
        return result;
    }

    public static List<SessionRecord> SessionsOverlapping(
        IReadOnlyList<SessionRecord> sessions,
        DateTimeOffset from,
        DateTimeOffset to,
        DateTimeOffset now)
    {
        return sessions
            .Where(s => TimeUtil.Overlaps(s.Start, s.End, from, to, now))
            .OrderByDescending(s => s.Start)
            .ToList();
    }
}
