using System;
using System.Windows.Data;

namespace FluentFlyoutWPF.Classes.Utils;

public class EqualityToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value == null || parameter == null)
            return false;

        return value.ToString() == parameter.ToString();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is true && parameter != null)
        {
            // Convert the parameter back to the target type (usually int)
            if (targetType == typeof(int) && int.TryParse(parameter.ToString(), out int intVal))
            {
                return intVal;
            }
            return parameter;
        }
        return Binding.DoNothing;
    }
}
