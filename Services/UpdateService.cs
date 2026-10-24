using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace ShiJi.Services;

public sealed class UpdateCheckResult
{
    public bool UpdateAvailable { get; init; }
    public string LatestVersion { get; init; } = "";
    public string ReleaseUrl { get; init; } = "";
    public string? Error { get; init; }
    public bool FromCache { get; init; }
}

public static class UpdateService
{
    private const string LatestReleaseApi =
        "https://api.github.com/repos/muchengugugu/ShiJi/releases/latest";
    private const string ReleasesPage =
        "https://github.com/muchengugugu/ShiJi/releases/latest";

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static UpdateState _state = new();
    private static bool _loaded;

    public static Version CurrentVersion =>
        typeof(UpdateService).Assembly.GetName().Version ?? new Version(1, 0, 0);

    public static string CurrentVersionText => CurrentVersion.ToString(3);

    public static UpdateCheckResult CachedResult()
    {
        LoadState();
        return BuildResult(fromCache: true);
    }

    public static async Task<UpdateCheckResult> CheckAsync(bool force)
    {
        await Gate.WaitAsync();
        try
        {
            LoadState();
            var now = DateTimeOffset.UtcNow;
            var fresh = _state.LastCheckUtc is { } last && now - last < TimeSpan.FromHours(24);
            if (!force && fresh && !string.IsNullOrWhiteSpace(_state.LatestTag))
            {
                return BuildResult(fromCache: true);
            }

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd(
                    $"ShiJi/{CurrentVersionText} (+https://github.com/muchengugugu/ShiJi)");
                using var response = await http.GetAsync(LatestReleaseApi);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean())
                    return BuildResult(fromCache: false);
                if (root.TryGetProperty("prerelease", out var pre) && pre.GetBoolean())
                    return BuildResult(fromCache: false);

                var tag = root.TryGetProperty("tag_name", out var tagElement)
                    ? tagElement.GetString()
                    : null;
                var url = root.TryGetProperty("html_url", out var urlElement)
                    ? urlElement.GetString()
                    : null;
                var latest = ParseVersion(tag);
                if (latest is null)
                    return BuildResult(fromCache: false, "无法识别最新版本号。");

                _state.LastCheckUtc = now;
                _state.LatestTag = tag;
                _state.ReleaseUrl = string.IsNullOrWhiteSpace(url) ? ReleasesPage : url;
                SaveState();
                return BuildResult(fromCache: false);
            }
            catch (Exception ex)
            {
                App.Log(ex);
                var cached = BuildResult(fromCache: true, ex.Message);
                return cached;
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    public static void Dismiss(string? latestVersion)
    {
        if (string.IsNullOrWhiteSpace(latestVersion)) return;
        LoadState();
        _state.DismissedTag = latestVersion;
        SaveState();
    }

    public static bool IsDismissed(string? latestVersion)
    {
        LoadState();
        return !string.IsNullOrWhiteSpace(latestVersion) &&
               string.Equals(_state.DismissedTag, latestVersion, StringComparison.OrdinalIgnoreCase);
    }

    public static bool OpenReleasePage(UpdateCheckResult result)
    {
        var url = string.IsNullOrWhiteSpace(result.ReleaseUrl)
            ? ReleasesPage
            : result.ReleaseUrl;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex)
        {
            App.Log(ex);
            return false;
        }
    }

    private static UpdateCheckResult BuildResult(bool fromCache, string? error = null)
    {
        var current = CurrentVersion;
        var latest = ParseVersion(_state.LatestTag);
        var available = latest is not null && latest > current;
        return new UpdateCheckResult
        {
            UpdateAvailable = available,
            LatestVersion = _state.LatestTag ?? "",
            ReleaseUrl = string.IsNullOrWhiteSpace(_state.ReleaseUrl)
                ? ReleasesPage
                : _state.ReleaseUrl,
            Error = error,
            FromCache = fromCache
        };
    }

    private static Version? ParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var value = tag.Trim();
        if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            value = value[1..];
        var dash = value.IndexOf('-');
        if (dash >= 0) value = value[..dash];
        return Version.TryParse(value, out var version) ? version : null;
    }

    private static string StateFile => Path.Combine(App.Store.BaseDirectory, "update-state.json");

    private static void LoadState()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (!File.Exists(StateFile)) return;
            var json = File.ReadAllText(StateFile);
            _state = JsonSerializer.Deserialize<UpdateState>(json, JsonOptions) ?? new UpdateState();
        }
        catch (Exception ex)
        {
            App.Log(ex);
            _state = new UpdateState();
        }
    }

    private static void SaveState()
    {
        try
        {
            Directory.CreateDirectory(App.Store.BaseDirectory);
            var temp = StateFile + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_state, JsonOptions));
            if (File.Exists(StateFile)) File.Delete(StateFile);
            File.Move(temp, StateFile);
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    private sealed class UpdateState
    {
        [JsonPropertyName("lastCheckUtc")]
        public DateTimeOffset? LastCheckUtc { get; set; }

        [JsonPropertyName("latestTag")]
        public string? LatestTag { get; set; }

        [JsonPropertyName("releaseUrl")]
        public string? ReleaseUrl { get; set; }

        [JsonPropertyName("dismissedTag")]
        public string? DismissedTag { get; set; }
    }
}
