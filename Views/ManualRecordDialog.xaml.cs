using System.Windows;
using System.Windows.Controls;
using ShiJi.Models;
using ShiJi.Services;

namespace ShiJi.Views;

public partial class ManualRecordDialog : Window
{
    private readonly DateOnly _day;
    private readonly DateTimeOffset _now;
    private readonly string? _fixedAppId;
    private readonly bool _ready;

    public ManualRecordDialog(
        IReadOnlyList<WatchedApp> apps,
        string? fixedAppId,
        DateOnly day,
        DateTimeOffset now)
    {
        InitializeComponent();
        _day = day;
        _now = now;
        _fixedAppId = fixedAppId;

        DayText.Text = $"{day.Year}年{day.Month}月{day.Day}日 周{Fmt.Weekday(day)}";

        if (!string.IsNullOrWhiteSpace(fixedAppId))
        {
            var app = apps.FirstOrDefault(a => a.Id == fixedAppId);
            AppPickerPanel.Visibility = Visibility.Collapsed;
            FixedAppPanel.Visibility = Visibility.Visible;
            FixedAppText.Text = app?.Name ?? "未知应用";
        }
        else
        {
            AppCombo.ItemsSource = apps;
            if (apps.Count > 0) AppCombo.SelectedIndex = 0;
        }

        for (var hour = TimeUtil.DayStartHour; hour < 24; hour++)
            StartHourCombo.Items.Add(new ComboBoxItem { Content = hour.ToString("00"), Tag = hour });
        for (var minute = 0; minute < 60; minute++)
            StartMinuteCombo.Items.Add(new ComboBoxItem { Content = minute.ToString("00"), Tag = minute });
        for (var hour = 0; hour < 24; hour++)
            DurationHourCombo.Items.Add(new ComboBoxItem { Content = hour.ToString(), Tag = hour });
        for (var minute = 0; minute < 60; minute++)
            DurationMinuteCombo.Items.Add(new ComboBoxItem { Content = minute.ToString(), Tag = minute });

        var defaultStart = DefaultStart();
        SelectValue(StartHourCombo, defaultStart.Hour);
        SelectValue(StartMinuteCombo, defaultStart.Minute);
        SelectValue(DurationHourCombo, 0);
        SelectValue(DurationMinuteCombo, 30);

        _ready = true;
        UpdatePreview();
    }

    public string SelectedAppId { get; private set; } = "";
    public DateTimeOffset SessionStart { get; private set; }
    public TimeSpan Duration { get; private set; }

    private DateTime DefaultStart()
    {
        if (_day != TimeUtil.Today)
            return _day.ToDateTime(new TimeOnly(Math.Max(TimeUtil.DayStartHour, 9), 0));

        var localNow = _now.ToLocalTime().DateTime;
        var dayDate = _day.ToDateTime(TimeOnly.MinValue);
        if (localNow.Date > dayDate)
            return _day.ToDateTime(new TimeOnly(23, 0));

        var candidate = localNow.AddMinutes(-30);
        var dayStart = TimeUtil.LocalDayStart(_day).LocalDateTime;
        if (candidate < dayStart) candidate = dayStart;
        return candidate;
    }

    private static void SelectValue(ComboBox combo, int value)
    {
        foreach (var item in combo.Items)
        {
            if (item is ComboBoxItem { Tag: int tag } && tag == value)
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }

    private static int SelectedValue(ComboBox combo, int fallback)
    {
        return combo.SelectedItem is ComboBoxItem { Tag: int value } ? value : fallback;
    }

    private void Input_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        var startHour = SelectedValue(StartHourCombo, TimeUtil.DayStartHour);
        var startMinute = SelectedValue(StartMinuteCombo, 0);
        var durationHours = SelectedValue(DurationHourCombo, 0);
        var durationMinutes = SelectedValue(DurationMinuteCombo, 30);
        var start = _day.ToDateTime(new TimeOnly(startHour, startMinute));
        var end = start.AddHours(durationHours).AddMinutes(durationMinutes);
        EndPreviewText.Text = end.Date == start.Date
            ? $"结束时间：{end:HH:mm}"
            : $"结束时间：次日 {end:HH:mm}";
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var appId = _fixedAppId;
        if (string.IsNullOrWhiteSpace(appId))
            appId = AppCombo.SelectedValue as string;
        if (string.IsNullOrWhiteSpace(appId))
        {
            MessageBox.Show(this, "请选择一个应用。", "添加记录", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var startHour = SelectedValue(StartHourCombo, -1);
        var startMinute = SelectedValue(StartMinuteCombo, -1);
        var durationHours = SelectedValue(DurationHourCombo, 0);
        var durationMinutes = SelectedValue(DurationMinuteCombo, 0);
        if (startHour < 0 || startMinute < 0)
        {
            MessageBox.Show(this, "请设置开始时间。", "添加记录", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var duration = new TimeSpan(durationHours, durationMinutes, 0);
        if (duration <= TimeSpan.Zero)
        {
            MessageBox.Show(this, "时长必须大于 0。", "添加记录", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var local = _day.ToDateTime(new TimeOnly(startHour, startMinute), DateTimeKind.Unspecified);
        var start = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
        var now = DateTimeOffset.Now;
        if (start > now)
        {
            MessageBox.Show(this, "开始时间不能晚于现在。", "添加记录", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (start + duration > now)
        {
            MessageBox.Show(this, "结束时间不能晚于现在。", "添加记录", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SelectedAppId = appId;
        SessionStart = start;
        Duration = duration;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
