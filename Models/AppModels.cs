using System.Text.Json.Serialization;

namespace ShiJi.Models;

public sealed class WatchedApp
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string ExecutablePath { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public string Color { get; set; } = "#6C5CE7";
    public bool Enabled { get; set; } = true;
    public bool ShowInToday { get; set; } = true;
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonIgnore]
    public string Folder => string.IsNullOrWhiteSpace(ExecutablePath)
        ? ""
        : Path.GetDirectoryName(ExecutablePath) ?? "";

    public WatchedApp Clone() => (WatchedApp)MemberwiseClone();
}

public sealed class SessionRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string AppId { get; set; } = "";
    public string AppName { get; set; } = "";
    public string AppColor { get; set; } = "#6C5CE7";
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset? End { get; set; }
    public DateTimeOffset LastSeen { get; set; }
    public int PeakInstances { get; set; } = 1;

    /// <summary>Null for records tracked by ShiJi itself; "Tai" for imported history.</summary>
    public string? Source { get; set; }

    /// <summary>Stable identifier used to make imports idempotent.</summary>
    public string? ExternalId { get; set; }

    [JsonIgnore]
    public bool IsImported => !string.IsNullOrWhiteSpace(Source);

    [JsonIgnore]
    public bool IsOpen => End is null;

    public TimeSpan DurationAt(DateTimeOffset now)
    {
        var end = End ?? now;
        var span = end - Start;
        return span < TimeSpan.Zero ? TimeSpan.Zero : span;
    }

    public SessionRecord Clone() => (SessionRecord)MemberwiseClone();
}

public sealed class AppSettings
{
    public bool LaunchAtStartup { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool FirstTrayHintShown { get; set; }
    public string LastView { get; set; } = "overview";
    public string DurationMode { get; set; } = "auto";
    public int DayStartHour { get; set; }
}

public sealed class AppData
{
    public int Version { get; set; } = 1;
    public List<WatchedApp> Apps { get; set; } = new();
    public List<SessionRecord> Sessions { get; set; } = new();
    public AppSettings Settings { get; set; } = new();
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
