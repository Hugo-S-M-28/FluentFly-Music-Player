using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FluentFlyoutWPF.Classes.Utils;

public class ResponsiveCoverSizeConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 && values[0] is double width && values[1] is double height)
        {
            // Calculate a responsive square size for the album art
            // Target is 35% of height or 25% of width, clamped between 120 and 320.
            double sizeByHeight = height * 0.35;
            double sizeByWidth = width * 0.25;
            
            double targetSize = Math.Min(sizeByHeight, sizeByWidth);
            
            if (targetSize < 120) targetSize = 120;
            if (targetSize > 320) targetSize = 320;
            
            return targetSize;
        }
        return 300.0;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class ResponsiveCoverColumnWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 && values[0] is double width && values[1] is bool isPlaylistVisible)
        {
            // Collapse cover column if playlist is open and width < 950, or if width is < 650 generally
            if ((isPlaylistVisible && width < 950) || width < 650)
            {
                return new GridLength(0, GridUnitType.Pixel);
            }
            return new GridLength(1, GridUnitType.Star);
        }
        return new GridLength(1, GridUnitType.Star);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class ResponsiveLyricsColumnWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 && values[0] is double width && values[1] is bool isPlaylistVisible)
        {
            // If cover is collapsed, lyrics takes all star space. Otherwise, it takes 2* ratio.
            if ((isPlaylistVisible && width < 950) || width < 650)
            {
                return new GridLength(1, GridUnitType.Star);
            }
            return new GridLength(2, GridUnitType.Star);
        }
        return new GridLength(2, GridUnitType.Star);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class ResponsiveCoverVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 && values[0] is double width && values[1] is bool isPlaylistVisible)
        {
            if ((isPlaylistVisible && width < 950) || width < 650)
            {
                return Visibility.Collapsed;
            }
            return Visibility.Visible;
        }
        return Visibility.Visible;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class ResponsiveFontSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double width)
        {
            string mode = (parameter as string ?? string.Empty).Trim().ToLowerInvariant();

            double minWidth = 280.0;
            double maxWidth = 1180.0;
            double minSize = 24.0;
            double maxSize = 50.0;

            switch (mode)
            {
                case "full":
                    minSize = 30.0;
                    maxSize = 58.0;
                    break;
                case "compact":
                    minSize = 22.0;
                    maxSize = 42.0;
                    break;
            }

            double clampedWidth = Math.Clamp(width, minWidth, maxWidth);
            double progress = (clampedWidth - minWidth) / (maxWidth - minWidth);
            double size = minSize + ((maxSize - minSize) * progress);

            return Math.Round(size, 1);
        }

        return parameter as string == "full" ? 44.0 : 36.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class ResponsiveLineHeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var fontSizeConverter = new ResponsiveFontSizeConverter();
        double fontSize = value is double width
            ? System.Convert.ToDouble(fontSizeConverter.Convert(width, typeof(double), parameter, culture), culture)
            : 36.0;

        double factor = parameter as string == "full" ? 1.18 : 1.16;
        return Math.Round(fontSize * factor, 1);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class ResponsiveLyricsContentWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double width)
        {
            double padding = parameter != null
                ? System.Convert.ToDouble(parameter, culture)
                : 72.0;

            double targetWidth = width - padding;
            return Math.Clamp(targetWidth, 280.0, 800.0);
        }

        return 640.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class ResponsivePageContentMaxWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double width)
        {
            if (width <= 0)
            {
                return 1280.0;
            }

            double sideGutter = width switch
            {
                < 900 => 20.0,
                < 1400 => 32.0,
                _ => 48.0
            };

            double target = width - (sideGutter * 2);
            return Math.Clamp(target, 320.0, 1280.0);
        }

        return 1280.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class ResponsivePageMarginConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double width)
        {
            double margin = width switch
            {
                < 760 => 12.0,
                < 1200 => 18.0,
                < 1700 => 24.0,
                _ => 28.0
            };

            return new Thickness(margin);
        }

        return new Thickness(24);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class ResponsiveBannerTextWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double width)
        {
            double target = width switch
            {
                < 760 => width - 32.0,
                < 1200 => width * 0.58,
                _ => width * 0.5
            };

            return Math.Clamp(target, 260.0, 840.0);
        }

        return 450.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class ResponsiveSearchWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double width)
        {
            double target = width switch
            {
                < 900 => width - 260.0,
                < 1400 => width * 0.28,
                _ => width * 0.24
            };

            return Math.Clamp(target, 220.0, 520.0);
        }

        return 360.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class ResponsiveSidePanelWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double width)
        {
            double target = width switch
            {
                < 1200 => 260.0,
                < 1600 => 300.0,
                _ => 340.0
            };

            return target;
        }

        return 300.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class ResponsiveInlineControlWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double width)
        {
            string mode = (parameter as string ?? string.Empty).Trim().ToLowerInvariant();

            double target = mode switch
            {
                "compact" => width switch
                {
                    < 900 => 120.0,
                    < 1400 => 150.0,
                    _ => 180.0
                },
                "wide" => width switch
                {
                    < 900 => 220.0,
                    < 1400 => 320.0,
                    _ => 420.0
                },
                _ => width switch
                {
                    < 900 => 160.0,
                    < 1400 => 220.0,
                    _ => 300.0
                }
            };

            return target;
        }

        return 180.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class ResponsiveCompactVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double width)
        {
            bool collapseWhenCompact = !string.Equals(parameter as string, "inverse", StringComparison.OrdinalIgnoreCase);
            bool isCompact = width < 300;

            if (collapseWhenCompact)
            {
                return isCompact ? Visibility.Collapsed : Visibility.Visible;
            }

            return isCompact ? Visibility.Visible : Visibility.Collapsed;
        }

        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class ResponsiveColumnsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double width)
        {
            if (width < 550) return 1;
            if (width < 900) return 2;
            return 4;
        }
        return 4;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
