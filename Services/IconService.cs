using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ShiJi.Models;

namespace ShiJi.Services;

public static class IconService
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? GetIcon(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        return Cache.GetOrAdd(path, LoadIcon);
    }

    public static ImageSource? GetIcon(WatchedApp app) => GetIcon(app.ExecutablePath);

    private static ImageSource? LoadIcon(string path)
    {
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(path);
            if (icon is null) return null;
            var source = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(64, 64));
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
    }
}

public static class ColorUtil
{
    private static readonly ConcurrentDictionary<string, SolidColorBrush> BrushCache =
        new(StringComparer.OrdinalIgnoreCase);

    public static System.Windows.Media.Color Parse(string? hex, System.Windows.Media.Color? fallback = null)
    {
        var def = fallback ?? System.Windows.Media.Color.FromRgb(108, 92, 231);
        if (string.IsNullOrWhiteSpace(hex)) return def;
        try
        {
            return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
        }
        catch
        {
            return def;
        }
    }

    public static SolidColorBrush Brush(string? hex)
    {
        return BrushCache.GetOrAdd(hex ?? "#6C5CE7", key =>
        {
            var brush = new SolidColorBrush(Parse(key));
            brush.Freeze();
            return brush;
        });
    }

    public static SolidColorBrush WithAlpha(string? hex, byte alpha)
    {
        var color = Parse(hex);
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    public static SolidColorBrush BlendWithWhite(string? hex, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        var color = Parse(hex);
        byte Blend(byte c) => (byte)Math.Round(255 - (255 - c) * amount);
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(Blend(color.R), Blend(color.G), Blend(color.B)));
        brush.Freeze();
        return brush;
    }

    public static string Initial(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var trimmed = name.Trim();
        return char.IsLetterOrDigit(trimmed[0])
            ? trimmed[0].ToString().ToUpperInvariant()
            : "?";
    }
}
