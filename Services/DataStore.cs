using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ShiJi.Models;

namespace ShiJi.Services;

public sealed class AddAppsResult
{
    public int Added { get; init; }
    public int Duplicates { get; init; }
    public List<string> AddedNames { get; init; } = new();
    public List<string> DuplicateNames { get; init; } = new();
}

public sealed class DataStore
{
    private static readonly string[] Palette =
    {
        "#6C5CE7", "#4F7CFF", "#18B7A0", "#FF9F43", "#F45B8B",
        "#22B8CF", "#8B5CF6", "#FF6B6B", "#20C997", "#F7B731",
        "#5B8DEF", "#A55EEA", "#FD79A8", "#00B894", "#E17055"
    };

    private readonly object _sync = new();
    private readonly JsonSerializerOptions _json;

    public DataStore(string? dataDirectory = null)
    {
        BaseDirectory = dataDirectory ?? ResolveDefaultDirectory();
        DataFile = Path.Combine(BaseDirectory, "data.json");
        BackupFile = Path.Combine(BaseDirectory, "data.json.bak");
        _json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    }

    public string BaseDirectory { get; }
    public string DataFile { get; }
    public string BackupFile { get; }
    public string? LoadWarning { get; private set; }
    public string? LastSaveError { get; private set; }

    private AppData _data = new();
    public object SyncRoot => _sync;

    public static string ResolveDefaultDirectory()
    {
        var overrideDir = Environment.GetEnvironmentVariable("SHIJI_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(overrideDir)) return overrideDir;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ShiJi");
    }

    public void Load()
    {
        lock (_sync)
        {
            Directory.CreateDirectory(BaseDirectory);
            AppData? loaded = null;

            if (File.Exists(DataFile))
            {
                loaded = TryRead(DataFile);
                if (loaded is null) LoadWarning = "数据文件读取失败，已尝试使用备份。";
            }

            if (loaded is null && File.Exists(BackupFile))
            {
                loaded = TryRead(BackupFile);
                if (loaded is not null) LoadWarning = "已从备份恢复数据。";
            }

            if (loaded is null)
            {
                loaded = new AppData();
                if (File.Exists(DataFile) && LoadWarning is not null)
                {
                    var bad = DataFile + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    try { File.Move(DataFile, bad, true); } catch { }
                    LoadWarning += " 原文件已保留为 " + Path.GetFileName(bad) + "。";
                }
            }

            _data = Normalize(loaded);

            var recovered = 0;
            var now = DateTimeOffset.UtcNow;
            foreach (var session in _data.Sessions.Where(s => s.End is null))
            {
                var end = session.LastSeen;
                if (end < session.Start) end = session.Start;
                if (end > now) end = now;
                session.End = end;
                session.LastSeen = end;
                recovered++;
            }

            if (recovered > 0)
            {
                try { SaveLocked(); } catch (Exception ex) { LastSaveError = ex.Message; }
            }
        }
    }

    private AppData? TryRead(string path)
    {
        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json)) return null;
            return JsonSerializer.Deserialize<AppData>(json, _json);
        }
        catch
        {
            return null;
        }
    }

    private static AppData Normalize(AppData data)
    {
        data.Apps ??= new List<WatchedApp>();
        data.Sessions ??= new List<SessionRecord>();
        data.Settings ??= new AppSettings();

        foreach (var app in data.Apps)
        {
            if (string.IsNullOrWhiteSpace(app.Id)) app.Id = Guid.NewGuid().ToString("N");
            if (string.IsNullOrWhiteSpace(app.ProcessName))
                app.ProcessName = Path.GetFileNameWithoutExtension(app.ExecutablePath);
            if (string.IsNullOrWhiteSpace(app.Color)) app.Color = "#6C5CE7";
            if (app.AddedAt == default) app.AddedAt = DateTimeOffset.UtcNow;
        }

        foreach (var session in data.Sessions)
        {
            if (string.IsNullOrWhiteSpace(session.Id)) session.Id = Guid.NewGuid().ToString("N");
            if (string.IsNullOrWhiteSpace(session.AppName))
            {
                session.AppName = data.Apps.FirstOrDefault(a => a.Id == session.AppId)?.Name ?? "未知应用";
            }
            if (string.IsNullOrWhiteSpace(session.AppColor)) session.AppColor = "#6C5CE7";
            if (session.LastSeen == default) session.LastSeen = session.End ?? session.Start;
            if (session.PeakInstances < 1) session.PeakInstances = 1;
        }

        // Remove exact duplicate IDs that could come from a hand-edited file.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        data.Apps = data.Apps.Where(a => seen.Add(a.Id)).ToList();
        seen.Clear();
        data.Sessions = data.Sessions.Where(s => seen.Add(s.Id)).OrderBy(s => s.Start).ToList();
        return data;
    }

    public List<WatchedApp> GetApps()
    {
        lock (_sync)
        {
            return _data.Apps
                .OrderByDescending(a => a.Enabled)
                .ThenBy(a => a.AddedAt)
                .Select(a => a.Clone())
                .ToList();
        }
    }

    public WatchedApp? GetApp(string id)
    {
        lock (_sync)
        {
            return _data.Apps.FirstOrDefault(a => a.Id == id)?.Clone();
        }
    }

    public List<SessionRecord> GetSessions()
    {
        lock (_sync)
        {
            return _data.Sessions
                .OrderByDescending(s => s.Start)
                .Select(s => s.Clone())
                .ToList();
        }
    }

    public SessionRecord? GetSession(string id)
    {
        lock (_sync)
        {
            return _data.Sessions.FirstOrDefault(s => s.Id == id)?.Clone();
        }
    }

    public AddAppsResult AddApps(IEnumerable<string> paths)
    {
        var added = new List<string>();
        var duplicates = new List<string>();
        lock (_sync)
        {
            foreach (var raw in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var path = NormalizePath(raw);
                if (path is null) continue;
                var processName = Path.GetFileNameWithoutExtension(path);
                if (_data.Apps.Any(a =>
                        string.Equals(NormalizePath(a.ExecutablePath), path, StringComparison.OrdinalIgnoreCase) ||
                        (string.IsNullOrWhiteSpace(a.ExecutablePath) &&
                         string.Equals(a.ProcessName, processName, StringComparison.OrdinalIgnoreCase))))
                {
                    duplicates.Add(processName);
                    continue;
                }

                var app = BuildApp(path, _data.Apps.Count);
                _data.Apps.Add(app);
                added.Add(app.Name);
            }

            if (added.Count > 0)
            {
                _data.UpdatedAt = DateTimeOffset.UtcNow;
                SaveLocked();
            }
        }

        if (added.Count > 0) RaiseChanged();
        return new AddAppsResult
        {
            Added = added.Count,
            Duplicates = duplicates.Count,
            AddedNames = added,
            DuplicateNames = duplicates
        };
    }

    /// <summary>
    /// Adds processes found in the running-process picker. When Windows denies
    /// access to the executable path, the app is stored with an empty path and
    /// only its process name; TrackerService then matches by name.
    /// </summary>
    public AddAppsResult AddDetectedApps(IEnumerable<DetectedApp> detected)
    {
        var added = new List<string>();
        var duplicates = new List<string>();
        lock (_sync)
        {
            foreach (var candidate in detected)
            {
                var path = NormalizePath(candidate.ExecutablePath);
                var processName = path is not null
                    ? Path.GetFileNameWithoutExtension(path)
                    : (candidate.ProcessName ?? "").Trim();
                if (string.IsNullOrWhiteSpace(processName)) continue;

                var duplicate = _data.Apps.Any(a =>
                {
                    var existingPath = NormalizePath(a.ExecutablePath);
                    if (!string.IsNullOrWhiteSpace(existingPath) && path is not null)
                        return string.Equals(existingPath, path, StringComparison.OrdinalIgnoreCase);
                    return string.Equals(a.ProcessName, processName, StringComparison.OrdinalIgnoreCase);
                });

                if (duplicate)
                {
                    duplicates.Add(candidate.Name?.Trim() is { Length: > 0 } n ? n : processName);
                    continue;
                }

                var name = (candidate.Name ?? "").Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = path is not null ? NameFromFile(path) : processName;
                }
                if (name.Length > 70) name = name[..70];

                _data.Apps.Add(new WatchedApp
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = name,
                    ExecutablePath = path ?? "",
                    ProcessName = processName,
                    Color = Palette[_data.Apps.Count % Palette.Length],
                    Enabled = true,
                    AddedAt = DateTimeOffset.UtcNow
                });
                added.Add(name);
            }

            if (added.Count > 0)
            {
                _data.UpdatedAt = DateTimeOffset.UtcNow;
                SaveLocked();
            }
        }

        if (added.Count > 0) RaiseChanged();
        return new AddAppsResult
        {
            Added = added.Count,
            Duplicates = duplicates.Count,
            AddedNames = added,
            DuplicateNames = duplicates
        };
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var full = Path.GetFullPath(path.Trim().Trim('"'));
            return File.Exists(full) ? full : null;
        }
        catch
        {
            return null;
        }
    }

    private static string NameFromFile(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            var product = info.ProductName?.Trim();
            var desc = info.FileDescription?.Trim();
            if (!string.IsNullOrWhiteSpace(product) && product.Length <= 60) name = product;
            else if (!string.IsNullOrWhiteSpace(desc) && desc.Length <= 60) name = desc;
        }
        catch { }
        return name;
    }

    private WatchedApp BuildApp(string path, int index)
    {
        var name = NameFromFile(path);

        return new WatchedApp
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            ExecutablePath = path,
            ProcessName = Path.GetFileNameWithoutExtension(path),
            Color = Palette[index % Palette.Length],
            Enabled = true,
            AddedAt = DateTimeOffset.UtcNow
        };
    }

    public void UpdateApp(string id, Action<WatchedApp> update)
    {
        var changed = false;
        lock (_sync)
        {
            var app = _data.Apps.FirstOrDefault(a => a.Id == id);
            if (app is null) return;
            update(app);
            _data.UpdatedAt = DateTimeOffset.UtcNow;
            SaveLocked();
            changed = true;
        }
        if (changed) RaiseChanged();
    }

    public void SetAppShown(string id, bool shown)
    {
        var changed = false;
        lock (_sync)
        {
            var app = _data.Apps.FirstOrDefault(a => a.Id == id);
            if (app is null || app.ShowInToday == shown) return;
            app.ShowInToday = shown;
            _data.UpdatedAt = DateTimeOffset.UtcNow;
            SaveLocked();
            changed = true;
        }
        if (changed) RaiseChanged();
    }

    public void SetAppEnabled(string id, bool enabled)
    {        var changed = false;
        lock (_sync)
        {
            var app = _data.Apps.FirstOrDefault(a => a.Id == id);
            if (app is null) return;
            app.Enabled = enabled;
            if (!enabled)
            {
                var now = DateTimeOffset.UtcNow;
                foreach (var session in _data.Sessions.Where(s => s.AppId == id && s.End is null))
                {
                    if (now < session.Start) continue;
                    session.End = now;
                    session.LastSeen = now;
                }
            }
            _data.UpdatedAt = DateTimeOffset.UtcNow;
            SaveLocked();
            changed = true;
        }
        if (changed) RaiseChanged();
    }

    public void RemoveApp(string id, bool deleteHistory)
    {
        var changed = false;
        lock (_sync)
        {
            var app = _data.Apps.FirstOrDefault(a => a.Id == id);
            if (app is null) return;
            _data.Apps.Remove(app);
            if (deleteHistory)
            {
                _data.Sessions.RemoveAll(s => s.AppId == id);
            }
            else
            {
                var now = DateTimeOffset.UtcNow;
                foreach (var session in _data.Sessions.Where(s => s.AppId == id && s.End is null))
                {
                    if (now < session.Start) continue;
                    session.End = now;
                    session.LastSeen = now;
                }
            }
            _data.UpdatedAt = DateTimeOffset.UtcNow;
            SaveLocked();
            changed = true;
        }
        if (changed) RaiseChanged();
    }

    public SessionRecord? OpenSession(string appId, DateTimeOffset start, DateTimeOffset now, int instances = 1)
    {
        SessionRecord? session = null;
        lock (_sync)
        {
            var app = _data.Apps.FirstOrDefault(a => a.Id == appId);
            if (app is null) return null;
            if (_data.Sessions.Any(s => s.AppId == appId && s.End is null)) return null;

            var safeStart = start;
            if (safeStart > now) safeStart = now;
            if (safeStart < app.AddedAt) safeStart = app.AddedAt;

            session = new SessionRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                AppId = app.Id,
                AppName = app.Name,
                AppColor = app.Color,
                Start = safeStart,
                End = null,
                LastSeen = now,
                PeakInstances = Math.Max(1, instances)
            };
            _data.Sessions.Add(session);
            _data.UpdatedAt = now;
        }

        try { Save(); } catch (Exception ex) { LastSaveError = ex.Message; }
        RaiseChanged();
        return session.Clone();
    }

    public void CloseSession(string sessionId, DateTimeOffset end)
    {
        var changed = false;
        lock (_sync)
        {
            var session = _data.Sessions.FirstOrDefault(s => s.Id == sessionId);
            if (session is null || session.End is not null) return;
            if (end < session.Start) end = session.Start;
            session.End = end;
            session.LastSeen = end;
            _data.UpdatedAt = end;
            changed = true;
        }

        if (!changed) return;
        try { Save(); } catch (Exception ex) { LastSaveError = ex.Message; }
        RaiseChanged();
    }

    public void TouchSessions(IReadOnlyDictionary<string, int> instancesBySession, DateTimeOffset now)
    {
        lock (_sync)
        {
            foreach (var pair in instancesBySession)
            {
                var session = _data.Sessions.FirstOrDefault(s => s.Id == pair.Key);
                if (session is null || session.End is not null) continue;
                session.LastSeen = now;
                if (pair.Value > session.PeakInstances) session.PeakInstances = pair.Value;
            }
        }
    }

    public void Save()
    {
        lock (_sync)
        {
            _data.UpdatedAt = DateTimeOffset.UtcNow;
            SaveLocked();
        }
    }

    private void SaveLocked()
    {
        Directory.CreateDirectory(BaseDirectory);
        var json = JsonSerializer.Serialize(_data, _json);
        var tmp = DataFile + ".tmp";
        File.WriteAllText(tmp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        if (File.Exists(DataFile))
        {
            try { File.Replace(tmp, DataFile, BackupFile, ignoreMetadataErrors: true); }
            catch (IOException)
            {
                File.Copy(tmp, DataFile, overwrite: true);
                try { File.Delete(tmp); } catch { }
            }
            catch (UnauthorizedAccessException)
            {
                File.Copy(tmp, DataFile, overwrite: true);
                try { File.Delete(tmp); } catch { }
            }
        }
        else
        {
            File.Move(tmp, DataFile);
        }
        LastSaveError = null;
    }

    public void DeleteSession(string id)
    {
        var changed = false;
        lock (_sync)
        {
            var session = _data.Sessions.FirstOrDefault(s => s.Id == id);
            if (session is null) return;
            if (session.End is null) return;
            _data.Sessions.Remove(session);
            _data.UpdatedAt = DateTimeOffset.UtcNow;
            SaveLocked();
            changed = true;
        }
        if (changed) RaiseChanged();
    }

    public int DeleteSessions(IEnumerable<string> ids)
    {
        var idSet = ids.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (idSet.Count == 0) return 0;
        var removed = 0;
        lock (_sync)
        {
            removed = _data.Sessions.RemoveAll(s => idSet.Contains(s.Id) && s.End is not null);
            if (removed > 0)
            {
                _data.UpdatedAt = DateTimeOffset.UtcNow;
                SaveLocked();
            }
        }
        if (removed > 0) RaiseChanged();
        return removed;
    }

    public int ClearCompletedSessions()
    {
        var removed = 0;
        lock (_sync)
        {
            removed = _data.Sessions.RemoveAll(s => s.End is not null);
            if (removed > 0)
            {
                _data.UpdatedAt = DateTimeOffset.UtcNow;
                SaveLocked();
            }
        }
        if (removed > 0) RaiseChanged();
        return removed;
    }

    public void UpdateSettings(Action<AppSettings> update)
    {
        lock (_sync)
        {
            update(_data.Settings);
            _data.UpdatedAt = DateTimeOffset.UtcNow;
            SaveLocked();
        }
    }

    public AppSettings GetSettings()
    {
        lock (_sync)
        {
            return new AppSettings
            {
                LaunchAtStartup = _data.Settings.LaunchAtStartup,
                MinimizeToTray = _data.Settings.MinimizeToTray,
                FirstTrayHintShown = _data.Settings.FirstTrayHintShown,
                LastView = _data.Settings.LastView,
                DurationMode = _data.Settings.DurationMode,
                DayStartHour = _data.Settings.DayStartHour
            };
        }
    }

    public int CountOpenSessions()
    {
        lock (_sync) return _data.Sessions.Count(s => s.End is null);
    }

    public Dictionary<string, int> GetOpenSessionIds()
    {
        lock (_sync)
        {
            return _data.Sessions
                .Where(s => s.End is null)
                .ToDictionary(s => s.AppId, s => 1);
        }
    }

    public event EventHandler? Changed;

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
