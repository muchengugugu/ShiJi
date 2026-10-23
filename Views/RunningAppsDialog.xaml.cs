using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ShiJi.Models;
using ShiJi.Services;

namespace ShiJi.Views;

public partial class RunningAppsDialog : Window
{
    private readonly HashSet<string> _existingPaths;
    private readonly HashSet<string> _existingProcessNames;
    private readonly HashSet<string> _existingNameOnly;
    private readonly List<RunningItem> _items = new();
    private ICollectionView? _view;
    private bool _showBackground;
    private bool _ready;
    private bool _loading;

    public RunningAppsDialog(IReadOnlyList<WatchedApp> existingApps)
    {
        InitializeComponent();
        _existingPaths = existingApps
            .Select(a => ProcessPathResolver.Normalize(a.ExecutablePath))
            .Where(p => p is not null)
            .Select(p => p!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _existingProcessNames = existingApps
            .Select(a => a.ProcessName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _existingNameOnly = existingApps
            .Where(a => string.IsNullOrWhiteSpace(a.ExecutablePath))
            .Select(a => a.ProcessName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Loaded += async (_, _) =>
        {
            _ready = true;
            await LoadAsync();
        };
    }

    public List<DetectedApp> SelectedApps { get; } = new();

    private async Task LoadAsync()
    {
        if (_loading) return;
        _loading = true;
        LoadingPanel.Visibility = Visibility.Visible;
        NoResultPanel.Visibility = Visibility.Collapsed;
        RefreshButton.IsEnabled = false;

        try
        {
            var rows = await Task.Run(ScanProcesses);
            _items.Clear();
            foreach (var row in rows)
            {
                var alreadyAdded = row.Path is not null
                    ? _existingPaths.Contains(row.Path) || _existingNameOnly.Contains(row.ProcessName)
                    : _existingProcessNames.Contains(row.ProcessName);
                var item = new RunningItem(row, alreadyAdded);
                item.PropertyChanged += Item_PropertyChanged;
                _items.Add(item);
            }

            _view = CollectionViewSource.GetDefaultView(_items);
            _view.Filter = FilterItem;
            ProcessList.ItemsSource = _view;
            UpdateCounts();
            UpdateNoResult();
            UpdateSelected();
        }
        catch (Exception ex)
        {
            App.Log(ex);
            NoResultText.Text = "读取进程失败：" + ex.Message;
            NoResultPanel.Visibility = Visibility.Visible;
        }
        finally
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            RefreshButton.IsEnabled = true;
            _loading = false;
            SearchBox.Focus();
        }
    }

    private static List<ProcessRow> ScanProcesses()
    {
        var map = new Dictionary<string, ProcessAccumulator>(StringComparer.OrdinalIgnoreCase);
        var own = Environment.ProcessId;
        Process[] all;
        try { all = Process.GetProcesses(); }
        catch { return new List<ProcessRow>(); }

        foreach (var process in all)
        {
            try
            {
                if (process.Id == own) continue;

                if (process.Id <= 4) continue;

                string processName;
                try { processName = process.ProcessName; }
                catch { continue; }
                if (string.IsNullOrWhiteSpace(processName)) continue;
                if (processName.Equals("Idle", StringComparison.OrdinalIgnoreCase) ||
                    processName.Equals("System", StringComparison.OrdinalIgnoreCase) ||
                    processName.Equals("Registry", StringComparison.OrdinalIgnoreCase))
                    continue;

                var path = ProcessPathResolver.TryGetPath(process);
                if (path is not null) path = ProcessPathResolver.Normalize(path);

                var hasWindow = false;
                var windowTitle = "";
                try
                {
                    hasWindow = process.MainWindowHandle != IntPtr.Zero;
                    windowTitle = process.MainWindowTitle?.Trim() ?? "";
                }
                catch { }

                // A process with a title but no MainWindowHandle is still a UI
                // process (some launchers and games behave this way).
                hasWindow = hasWindow || !string.IsNullOrWhiteSpace(windowTitle);

                var key = path is not null ? "P|" + path : "N|" + processName;
                var displayName = DisplayName(path, processName, windowTitle);
                if (!map.TryGetValue(key, out var item))
                {
                    item = new ProcessAccumulator
                    {
                        Path = path,
                        ProcessName = processName,
                        Name = displayName,
                        Count = 0,
                        Visible = false
                    };
                    map[key] = item;
                }

                item.Count++;
                item.Visible |= hasWindow;
                if (string.IsNullOrWhiteSpace(item.WindowTitle) && !string.IsNullOrWhiteSpace(windowTitle))
                    item.WindowTitle = windowTitle;
            }
            catch { }
            finally
            {
                process.Dispose();
            }
        }

        return map.Values
            .OrderByDescending(v => v.Visible)
            .ThenBy(v => v.Visible && v.Path is null ? 0 : 1)
            .ThenBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(v => new ProcessRow(
                v.Path,
                v.ProcessName,
                v.Name,
                v.Count,
                v.Visible,
                v.Path is null))
            .ToList();
    }

    private static string DisplayName(string? path, string processName, string? windowTitle)
    {
        var name = processName;
        if (path is not null)
        {
            name = Path.GetFileNameWithoutExtension(path);
            try
            {
                var info = FileVersionInfo.GetVersionInfo(path);
                if (!string.IsNullOrWhiteSpace(info.FileDescription)) name = info.FileDescription.Trim();
                else if (!string.IsNullOrWhiteSpace(info.ProductName)) name = info.ProductName.Trim();
            }
            catch { }
        }
        else if (!string.IsNullOrWhiteSpace(windowTitle))
        {
            // For protected games, the window title is often more meaningful
            // than a generic game executable name.
            name = windowTitle;
        }

        name = name.Trim();
        return name.Length > 70 ? name[..70] : name;
    }

    private bool FilterItem(object item)
    {
        if (item is not RunningItem row) return false;
        var query = SearchBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(query))
        {
            // Search covers background and protected processes as well, so a
            // game that has no visible top-level window can still be found.
            return row.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                   row.ProcessName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                   row.PathDisplay.Contains(query, StringComparison.OrdinalIgnoreCase);
        }

        return _showBackground || row.HasWindow;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        if (!_ready || _view is null) return;
        _view.Refresh();
        UpdateNoResult();
    }

    private void ShowBackground_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready || _view is null) return;
        _showBackground = ShowBackground.IsChecked == true;
        _view.Refresh();
        UpdateCounts();
        UpdateNoResult();
    }

    private void UpdateCounts()
    {
        var hidden = _items.Count(i => !i.HasWindow);
        TotalCountText.Text = $"共 {_items.Count} 个进程";
        HiddenCountText.Text = !_showBackground && hidden > 0 ? $"（另有 {hidden} 个后台）" : "";
    }

    private void UpdateNoResult()
    {
        if (_view is null) return;
        var count = _view.Cast<object>().Count();
        NoResultPanel.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!string.IsNullOrWhiteSpace(SearchBox.Text))
        {
            NoResultText.Text = "没有匹配的程序，试试进程名或窗口标题的一部分。";
            return;
        }

        var hidden = _items.Count(i => !i.HasWindow);
        NoResultText.Text = !_showBackground && hidden > 0
            ? $"没有可添加的前台程序；打开“显示后台进程”查看另外 {hidden} 个。"
            : "没有可添加的程序";
    }

    private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RunningItem.IsSelected)) UpdateSelected();
    }

    private void UpdateSelected()
    {
        var count = _items.Count(i => i.IsSelected);
        SelectedCountText.Text = $"已选择 {count} 个";
        AddButton.IsEnabled = count > 0;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        if (_view is null) return;
        var select = SelectAllBox.IsChecked == true;
        foreach (var item in _view.Cast<RunningItem>().Where(i => i.CanSelect))
            item.IsSelected = select;
        UpdateSelected();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = LoadAsync();

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        SelectedApps.Clear();
        SelectedApps.AddRange(_items
            .Where(i => i.IsSelected && i.CanSelect)
            .Select(i => new DetectedApp
            {
                Name = i.Name,
                ProcessName = i.ProcessName,
                ExecutablePath = i.Path
            }));
        if (SelectedApps.Count == 0) return;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private sealed class ProcessAccumulator
    {
        public string? Path { get; init; }
        public string ProcessName { get; init; } = "";
        public string Name { get; init; } = "";
        public int Count { get; set; }
        public bool Visible { get; set; }
        public string WindowTitle { get; set; } = "";
    }

    private sealed record ProcessRow(
        string? Path,
        string ProcessName,
        string Name,
        int Count,
        bool Visible,
        bool PathProtected);

    private sealed class RunningItem : INotifyPropertyChanged
    {
        private bool _selected;

        public RunningItem(ProcessRow row, bool alreadyAdded)
        {
            Path = row.Path;
            ProcessName = row.ProcessName;
            Name = row.Name;
            HasWindow = row.Visible;
            AlreadyAdded = alreadyAdded;
            CountText = row.Count > 1 ? $"{row.Count} 个" : "1";
            Icon = row.Path is null ? null : IconService.GetIcon(row.Path);
            Initial = ColorUtil.Initial(row.Name);
            BadgeBackground = ColorUtil.Brush("#F3F1FF");
            BadgeForeground = ColorUtil.Brush("#6A5AE0");
            PathDisplay = row.Path is null
                ? "路径受保护 · 按进程名匹配"
                : row.Path;

            if (alreadyAdded)
            {
                StateText = "已在名单";
                StateBackground = ColorUtil.Brush("#F3F3F8");
                StateForeground = ColorUtil.Brush("#8A8C9C");
            }
            else if (row.Path is null)
            {
                StateText = "受保护";
                StateBackground = ColorUtil.Brush("#FFF5E6");
                StateForeground = ColorUtil.Brush("#B26A00");
            }
            else if (HasWindow)
            {
                StateText = "有窗口";
                StateBackground = ColorUtil.Brush("#E9F9F2");
                StateForeground = ColorUtil.Brush("#0C9B6B");
            }
            else
            {
                StateText = "后台";
                StateBackground = ColorUtil.Brush("#F3F3F8");
                StateForeground = ColorUtil.Brush("#7C7E90");
            }
        }

        public string? Path { get; }
        public string PathDisplay { get; }
        public string ProcessName { get; }
        public string Name { get; }
        public bool HasWindow { get; }
        public bool AlreadyAdded { get; }
        public bool CanSelect => !AlreadyAdded;
        public string CountText { get; }
        public ImageSource? Icon { get; }
        public string Initial { get; }
        public Brush BadgeBackground { get; }
        public Brush BadgeForeground { get; }
        public string StateText { get; }
        public Brush StateBackground { get; }
        public Brush StateForeground { get; }

        public bool IsSelected
        {
            get => _selected;
            set
            {
                if (_selected == value) return;
                _selected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
