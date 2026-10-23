using System.Diagnostics;
using System.IO;
using ShiJi.Models;

namespace ShiJi.Services;

public sealed class TrackerService : IDisposable
{
    private readonly DataStore _store;
    private readonly int _intervalMs;
    private readonly object _stateSync = new();
    private readonly Dictionary<string, string> _openSessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _trackingSince = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _firstStart = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _instanceCounts = new(StringComparer.OrdinalIgnoreCase);

    private System.Threading.Timer? _timer;
    private DateTimeOffset _startedAt;
    private DateTimeOffset _lastHeartbeat;
    private int _ticking;
    private int _disposed;
    private bool _started;
    private bool _firstScanDone;

    public TrackerService(DataStore store, int intervalMs = 1000)
    {
        _store = store;
        _intervalMs = Math.Max(500, intervalMs);
    }

    public event EventHandler? StateChanged;

    public string? LastError { get; private set; }

    public void Start()
    {
        if (_started) return;
        _started = true;
        _startedAt = DateTimeOffset.UtcNow;
        _lastHeartbeat = DateTimeOffset.UtcNow;
        _timer = new System.Threading.Timer(_ => Tick(), null, 350, _intervalMs);
    }

    public void Kick()
    {
        try { _timer?.Change(50, _intervalMs); } catch (ObjectDisposedException) { }
    }

    private void Tick()
    {
        if (Interlocked.Exchange(ref _ticking, 1) == 1) return;
        try
        {
            ScanOnce();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
        finally
        {
            Interlocked.Exchange(ref _ticking, 0);
        }
    }

    private void ScanOnce()
    {
        if (Volatile.Read(ref _disposed) == 1) return;
        var now = DateTimeOffset.UtcNow;
        var apps = _store.GetApps().Where(a => a.Enabled).ToList();
        var byId = apps.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);

        lock (_stateSync)
        {
            foreach (var app in apps)
            {
                if (!_trackingSince.ContainsKey(app.Id))
                {
                    var since = _firstScanDone
                        ? now
                        : (app.AddedAt > _startedAt ? app.AddedAt : _startedAt);
                    _trackingSince[app.Id] = since;
                }
            }

            foreach (var stale in _trackingSince.Keys.Where(id => !byId.ContainsKey(id)).ToList())
                _trackingSince.Remove(stale);

            _firstScanDone = true;
        }

        var processNames = new HashSet<string>(
            apps.Where(a => !string.IsNullOrWhiteSpace(a.ProcessName)).Select(a => a.ProcessName),
            StringComparer.OrdinalIgnoreCase);

        var processes = CaptureProcesses(processNames);

        var active = new Dictionary<string, List<ProcessSnapshot>>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in apps)
        {
            var candidates = processes
                .Where(p => string.Equals(p.Name, app.ProcessName, StringComparison.OrdinalIgnoreCase))
                .Where(p => MatchesPath(p, app))
                .ToList();
            if (candidates.Count > 0) active[app.Id] = candidates;
        }

        var toOpen = new List<(WatchedApp App, DateTimeOffset Start, int Instances)>();
        var toClose = new List<string>();
        var touch = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        lock (_stateSync)
        {
            foreach (var app in apps)
            {
                if (active.TryGetValue(app.Id, out var matches))
                {
                    var earliest = matches
                        .Where(m => m.Start is not null)
                        .Select(m => m.Start!.Value)
                        .DefaultIfEmpty(now)
                        .Min();
                    var since = _trackingSince.TryGetValue(app.Id, out var s) ? s : now;
                    if (earliest < since) earliest = since;
                    _firstStart[app.Id] = earliest;
                    _instanceCounts[app.Id] = matches.Count;

                    if (!_openSessions.ContainsKey(app.Id))
                    {
                        toOpen.Add((app, earliest, matches.Count));
                    }
                    else
                    {
                        touch[_openSessions[app.Id]] = matches.Count;
                    }
                }
                else
                {
                    _instanceCounts.Remove(app.Id);
                    if (_openSessions.TryGetValue(app.Id, out var sessionId))
                    {
                        toClose.Add(sessionId);
                        _openSessions.Remove(app.Id);
                    }
                }
            }

            foreach (var staleAppId in _openSessions.Keys.Where(id => !byId.ContainsKey(id)).ToList())
            {
                toClose.Add(_openSessions[staleAppId]);
                _openSessions.Remove(staleAppId);
            }
        }

        foreach (var item in toOpen)
        {
            var session = _store.OpenSession(item.App.Id, item.Start, now, item.Instances);
            if (session is not null)
            {
                lock (_stateSync) _openSessions[item.App.Id] = session.Id;
            }
        }

        foreach (var sessionId in toClose)
        {
            _store.CloseSession(sessionId, now);
        }

        if (touch.Count > 0) _store.TouchSessions(touch, now);

        if (toOpen.Count > 0 || toClose.Count > 0)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
            _lastHeartbeat = now;
        }
        else if (touch.Count > 0 && (now - _lastHeartbeat).TotalSeconds >= 30)
        {
            try
            {
                _store.Save();
                _lastHeartbeat = now;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
        }
    }

    private static bool MatchesPath(ProcessSnapshot process, WatchedApp app)
    {
        if (string.IsNullOrWhiteSpace(app.ExecutablePath)) return true;
        if (string.IsNullOrWhiteSpace(process.Path)) return true;
        return string.Equals(
            Path.GetFullPath(process.Path),
            Path.GetFullPath(app.ExecutablePath),
            StringComparison.OrdinalIgnoreCase);
    }

    private static List<ProcessSnapshot> CaptureProcesses(HashSet<string> names)
    {
        var result = new List<ProcessSnapshot>();
        if (names.Count == 0) return result;

        Process[] all;
        try { all = Process.GetProcesses(); }
        catch { return result; }

        var ownPid = Environment.ProcessId;
        foreach (var process in all)
        {
            try
            {
                if (process.Id == ownPid) continue;
                string name;
                try { name = process.ProcessName; }
                catch { continue; }
                if (!names.Contains(name)) continue;

                string? path = null;
                DateTimeOffset? start = null;
                path = ProcessPathResolver.TryGetPath(process);
                try { start = new DateTimeOffset(process.StartTime).ToUniversalTime(); }
                catch { }

                result.Add(new ProcessSnapshot(name, path, start));
            }
            finally
            {
                process.Dispose();
            }
        }

        return result;
    }

    public HashSet<string> GetRunningAppIds()
    {
        lock (_stateSync)
        {
            return new HashSet<string>(_instanceCounts.Keys, StringComparer.OrdinalIgnoreCase);
        }
    }

    public int RunningAppCount
    {
        get { lock (_stateSync) return _instanceCounts.Count; }
    }

    public Dictionary<string, int> GetRunningInstances()
    {
        lock (_stateSync)
        {
            return new Dictionary<string, int>(_instanceCounts, StringComparer.OrdinalIgnoreCase);
        }
    }

    public void Stop()
    {
        if (_disposed == 1) return;
        _disposed = 1;
        try { _timer?.Dispose(); } catch { }
        _timer = null;

        var now = DateTimeOffset.UtcNow;
        List<string> open;
        lock (_stateSync)
        {
            open = _openSessions.Values.ToList();
            _openSessions.Clear();
        }

        foreach (var id in open) _store.CloseSession(id, now);
        try { _store.Save(); } catch (Exception ex) { LastError = ex.Message; }
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private readonly record struct ProcessSnapshot(string Name, string? Path, DateTimeOffset? Start);
}
