using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;

namespace FluentFlyoutWPF.Classes.Utils;

public enum AccentColorSource
{
    AlbumArt,
    Custom,
    Neutral
}

public static class AccentColorResolver
{
    public static AccentColorSource ResolveAccentSource(SolidColorBrush? albumArtBrush = null)
    {
        var settings = SettingsManager.Current;
        var resolvedAlbumArtBrush = albumArtBrush ?? BitmapHelper.SavedDominantColors.FirstOrDefault();

        return ResolveAccentSource(
            settings.UseAlbumArtAsAccentColor,
            settings.UseCustomAccentColor,
            settings.CustomAccentColorHex,
            BitmapHelper.HasAlbumArt,
            resolvedAlbumArtBrush);
    }

    public static AccentColorSource ResolveAccentSource(
        bool useAlbumArtAsAccentColor,
        bool useCustomAccentColor,
        string? customAccentColorHex,
        bool hasAlbumArt,
        SolidColorBrush? albumArtBrush = null)
    {
        if (useAlbumArtAsAccentColor && hasAlbumArt)
        {
            return AccentColorSource.AlbumArt;
        }

        if (useCustomAccentColor && TryParseCustomAccent(customAccentColorHex, out _))
        {
            return AccentColorSource.Custom;
        }

        return AccentColorSource.Neutral;
    }

    public static SolidColorBrush ResolveAccentBrush(SolidColorBrush? albumArtBrush = null)
    {
        var settings = SettingsManager.Current;
        var resolvedAlbumArtBrush = albumArtBrush ?? BitmapHelper.SavedDominantColors.FirstOrDefault();
        var source = ResolveAccentSource(
            settings.UseAlbumArtAsAccentColor,
            settings.UseCustomAccentColor,
            settings.CustomAccentColorHex,
            BitmapHelper.HasAlbumArt,
            resolvedAlbumArtBrush);

        return source switch
        {
            AccentColorSource.AlbumArt when resolvedAlbumArtBrush != null => resolvedAlbumArtBrush,
            AccentColorSource.Custom when TryParseCustomAccent(settings.CustomAccentColorHex, out var customBrush) => customBrush!,
            AccentColorSource.AlbumArt when settings.UseCustomAccentColor && TryParseCustomAccent(settings.CustomAccentColorHex, out var customBrush) => customBrush!,
            _ => ThemeResourceHelper.GetSecondaryTextSolidBrush()
        };
    }

    public static bool ShouldUseAccent(SolidColorBrush? albumArtBrush = null)
    {
        var settings = SettingsManager.Current;
        var resolvedAlbumArtBrush = albumArtBrush ?? BitmapHelper.SavedDominantColors.FirstOrDefault();
        var source = ResolveAccentSource(
            settings.UseAlbumArtAsAccentColor,
            settings.UseCustomAccentColor,
            settings.CustomAccentColorHex,
            BitmapHelper.HasAlbumArt,
            resolvedAlbumArtBrush);

        if (source == AccentColorSource.AlbumArt && resolvedAlbumArtBrush == null)
        {
            if (settings.UseCustomAccentColor && TryParseCustomAccent(settings.CustomAccentColorHex, out _))
            {
                return true;
            }
            return false;
        }

        return source != AccentColorSource.Neutral;
    }

    public static bool TryParseCustomAccent(string? hex, out SolidColorBrush? brush)
    {
        brush = null;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        string cleaned = hex.Trim();
        if (cleaned.StartsWith("#"))
        {
            cleaned = cleaned.Substring(1);
        }

        if (cleaned.Length != 6 && cleaned.Length != 8)
        {
            return false;
        }

        foreach (char c in cleaned)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
            {
                return false;
            }
        }

        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex.StartsWith("#") ? hex : "#" + hex);
            brush = new SolidColorBrush(color);
            brush.Freeze();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static SolidColorBrush ResolveReadableAccentBrush(SolidColorBrush? brush, bool isDarkTheme)
    {
        var activeBrush = ResolveAccentBrush(brush);
        return EnsureReadableBrush(activeBrush, isDarkTheme);
    }

    public static SolidColorBrush EnsureReadableBrush(SolidColorBrush inputBrush, bool isDarkTheme)
    {
        var color = inputBrush.Color;
        double luminance = ((0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B)) / 255d;

        if (isDarkTheme)
        {
            if (luminance < 0.16)
            {
                return Brushes.White;
            }
        }
        else
        {
            if (luminance > 0.23)
            {
                var newBrush = new SolidColorBrush(Color.FromRgb(18, 18, 18));
                newBrush.Freeze();
                return newBrush;
            }
        }

        return inputBrush;
    }
}
