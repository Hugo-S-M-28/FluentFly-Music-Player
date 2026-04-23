using System;
using System.Globalization;
using System.Windows.Data;

namespace FluentFlyoutWPF.Classes.Utils;

public class HalfValueConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double d = 0;
        if (value is double val)
        {
            d = val;
        }
        else if (value != null && double.TryParse(value.ToString(), out double parsed))
        {
            d = parsed;
        }

        return new System.Windows.CornerRadius(d / 2);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
