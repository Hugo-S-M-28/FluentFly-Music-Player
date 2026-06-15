using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FluentFlyoutWPF.Classes.Utils;

public class DoubleToCornerRadiusConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double d)
        {
            return new CornerRadius(d);
        }
        if (value is float f)
        {
            return new CornerRadius(f);
        }
        if (value is int i)
        {
            return new CornerRadius(i);
        }
        return new CornerRadius(0);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is CornerRadius cr)
        {
            return cr.TopLeft;
        }
        return 0.0;
    }
}
