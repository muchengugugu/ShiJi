using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using ShiJi.Models;
using ShiJi.Services;

namespace ShiJi.Views;

public static class Ui
{
    public static Border AppBadge(WatchedApp app, double size = 38) =>
        AppBadge(app.Name, app.Color, app.ExecutablePath, size);

    public static Border AppBadge(string name, string color, string? path, double size = 38)
    {
        var border = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size * 0.27),
            Background = ColorUtil.BlendWithWhite(color, 0.88),
            SnapsToDevicePixels = true
        };

        var source = IconService.GetIcon(path);
        if (source is not null)
        {
            border.Child = new Image
            {
                Source = source,
                Stretch = Stretch.Uniform,
                Width = size * 0.62,
                Height = size * 0.62,
                SnapsToDevicePixels = true
            };
        }
        else
        {
            border.Child = new TextBlock
            {
                Text = ColorUtil.Initial(name),
                FontSize = size * 0.42,
                FontWeight = FontWeights.SemiBold,
                Foreground = ColorUtil.Brush(color),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }
        return border;
    }

    public static Border Pill(string text, string foreground, string background, string? glyph = null)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (!string.IsNullOrEmpty(glyph))
        {
            panel.Children.Add(new TextBlock
            {
                Text = glyph,
                FontFamily = (FontFamily)System.Windows.Application.Current.FindResource("IconFont"),
                FontSize = 9.5,
                Foreground = ColorUtil.Brush(foreground),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            });
        }
        panel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 10.5,
            FontWeight = FontWeights.Medium,
            Foreground = ColorUtil.Brush(foreground),
            VerticalAlignment = VerticalAlignment.Center
        });
        return new Border
        {
            Background = ColorUtil.Brush(background),
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(9, 4, 9, 4),
            Child = panel,
            HorizontalAlignment = HorizontalAlignment.Left
        };
    }

    public static Border RunningPill(bool compact = false)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = 5,
            Height = 5,
            Fill = ColorUtil.Brush("#13B981"),
            Margin = new Thickness(0, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = compact ? "运行中" : "正在运行",
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = ColorUtil.Brush("#0C9B6B")
        });
        return new Border
        {
            Background = ColorUtil.Brush("#E9F9F2"),
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(9, 4, 9, 4),
            Child = panel,
            HorizontalAlignment = HorizontalAlignment.Left
        };
    }

    public static Border StatusPill(bool launched, bool running)
    {
        if (running) return RunningPill();
        return launched
            ? Pill("已启动", "#5A49D6", "#F0EEFF", "\uE73E")
            : Pill("未启动", "#9A9CAB", "#F3F3F7", "\uE738");
    }

    public static TextBlock Glyph(string glyph, double size, string color, double width = 0)
    {
        return new TextBlock
        {
            Text = glyph,
            FontFamily = (FontFamily)System.Windows.Application.Current.FindResource("IconFont"),
            FontSize = size,
            Foreground = ColorUtil.Brush(color),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Width = width
        };
    }

    public static Border IconSquare(string glyph, string color, double size = 42)
    {
        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size * 0.28),
            Background = ColorUtil.Brush(color),
            Child = new TextBlock
            {
                Text = glyph,
                FontFamily = (FontFamily)System.Windows.Application.Current.FindResource("IconFont"),
                FontSize = size * 0.42,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    public static TextBlock Title(string text, double size = 15) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = FontWeights.SemiBold,
        Foreground = ColorUtil.Brush("#1D1E2A"),
        VerticalAlignment = VerticalAlignment.Center
    };

    public static TextBlock Muted(string text, double size = 11.5) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = ColorUtil.Brush("#9A9CAB"),
        VerticalAlignment = VerticalAlignment.Center
    };

    public static Border Card(UIElement child, Thickness? padding = null, Thickness? margin = null)
    {
        return new Border
        {
            Style = (Style)System.Windows.Application.Current.FindResource("Card"),
            Padding = padding ?? new Thickness(20),
            Margin = margin ?? new Thickness(0),
            Child = child
        };
    }

    public static Border Separator() => new()
    {
        Height = 1,
        Background = ColorUtil.Brush("#EFEFF5"),
        Margin = new Thickness(0)
    };

    public static DropShadowEffect SubtleShadow() => new()
    {
        BlurRadius = 18,
        ShadowDepth = 3,
        Opacity = 0.08,
        Color = System.Windows.Media.Color.FromRgb(52, 42, 103)
    };

    public static TextBlock Ellipsis(string text, double maxWidth, double size = 11.5, string color = "#9A9CAB")
    {
        return new TextBlock
        {
            Text = text,
            FontSize = size,
            Foreground = ColorUtil.Brush(color),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = maxWidth,
            ToolTip = text,
            VerticalAlignment = VerticalAlignment.Center
        };
    }
}
