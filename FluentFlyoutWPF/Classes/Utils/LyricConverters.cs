using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace FluentFlyoutWPF.Classes.Utils;

public class LyricWordHighlightConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        // values[0]: CurrentPosition (TimeSpan)
        // values[1]: WordTime (TimeSpan)
        // values[2]: IsLineActive (bool)
        
        if (values.Length >= 3 && values[0] is TimeSpan currentPos && values[1] is TimeSpan wordTime && values[2] is bool isLineActive)
        {
            if (!isLineActive) return false;
            return currentPos >= wordTime;
        }
        return false;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
