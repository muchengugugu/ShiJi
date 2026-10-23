using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ShiJi.Models;
using ShiJi.Services;

namespace ShiJi.Views;

public partial class EditAppDialog : Window
{
    private static readonly string[] Palette =
    {
        "#6C5CE7", "#4F7CFF", "#18B7A0", "#FF9F43", "#F45B8B",
        "#22B8CF", "#8B5CF6", "#FF6B6B", "#20C997", "#F7B731",
        "#5B8DEF", "#A55EEA", "#FD79A8", "#00B894", "#E17055"
    };

    private readonly WatchedApp _app;
    private readonly List<Border> _swatches = new();
    private bool _ready;

    public EditAppDialog(WatchedApp app)
    {
        InitializeComponent();
        _app = app;
        AppColor = app.Color;
        NameBox.Text = app.Name;
        PathText.Text = string.IsNullOrWhiteSpace(app.ExecutablePath)
            ? "路径受保护，无法读取 exe；将按进程名“" + app.ProcessName + "”匹配"
            : app.ExecutablePath;
        UpdatePreview();
        BuildSwatches();
        _ready = true;
    }

    public string AppName => NameBox.Text.Trim();
    public string AppColor { get; private set; }

    private void BuildSwatches()
    {
        ColorPanel.Children.Clear();
        _swatches.Clear();
        foreach (var color in Palette)
        {
            var inner = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(13),
                Background = ColorUtil.Brush(color)
            };
            var outer = new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(17),
                BorderThickness = new Thickness(2),
                BorderBrush = string.Equals(color, AppColor, StringComparison.OrdinalIgnoreCase)
                    ? ColorUtil.Brush("#5848CD")
                    : Brushes.Transparent,
                Padding = new Thickness(2),
                Margin = new Thickness(0, 0, 8, 8),
                Cursor = Cursors.Hand,
                Tag = color,
                Child = inner
            };
            outer.MouseLeftButtonUp += (_, _) =>
            {
                AppColor = color;
                UpdateSwatchSelection();
                UpdatePreview();
            };
            _swatches.Add(outer);
            ColorPanel.Children.Add(outer);
        }
    }

    private void UpdateSwatchSelection()
    {
        foreach (var swatch in _swatches)
        {
            var selected = string.Equals(swatch.Tag as string, AppColor, StringComparison.OrdinalIgnoreCase);
            swatch.BorderBrush = selected ? ColorUtil.Brush("#5848CD") : Brushes.Transparent;
        }
    }

    private void UpdatePreview()
    {
        if (!_ready && PreviewHost is null) return;
        PreviewHost.Content = Ui.AppBadge(
            string.IsNullOrWhiteSpace(NameBox.Text) ? _app.Name : NameBox.Text.Trim(),
            AppColor,
            _app.ExecutablePath,
            54);
        PreviewName.Text = string.IsNullOrWhiteSpace(NameBox.Text) ? _app.Name : NameBox.Text.Trim();
    }

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        UpdatePreview();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            System.Windows.MessageBox.Show("显示名称不能为空。", "编辑应用", MessageBoxButton.OK, MessageBoxImage.Warning);
            NameBox.Focus();
            return;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
