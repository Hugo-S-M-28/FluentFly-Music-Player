using System;
using System.Globalization;
using System.Windows.Data;

namespace FluentFlyoutWPF.Classes.Utils
{
    public class SensitivityToPercentageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int sensitivity)
            {
                float minDb = -30f - (sensitivity * 5f);
                float level = (minDb + 80f) / 90f * 100f;
                
                if (parameter != null && double.TryParse(parameter.ToString(), out double width))
                {
                    return (level / 100.0) * width;
                }
                return (double)level;
            }
            return 0.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class PeakLevelToPercentageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int peak)
            {
                float maxDb = -45f + (peak * 5f);
                float level = (maxDb + 80f) / 90f * 100f;

                if (parameter != null && double.TryParse(parameter.ToString(), out double width))
                {
                    return (level / 100.0) * width;
                }
                return (double)level;
            }
            return 100.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class SensitivityToGridLengthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int sensitivity)
            {
                float minDb = -30f - (sensitivity * 5f);
                float level = (minDb + 80f) / 90f * 100f;
                
                if (level < 0) level = 0;
                if (level > 100) level = 100;
                
                return new System.Windows.GridLength(level, System.Windows.GridUnitType.Star);
            }
            return new System.Windows.GridLength(0, System.Windows.GridUnitType.Star);
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class PeakLevelToGridLengthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int peak)
            {
                float maxDb = -45f + (peak * 5f);
                float level = (maxDb + 80f) / 90f * 100f;

                if (level < 0) level = 0;
                if (level > 100) level = 100;

                return new System.Windows.GridLength(level, System.Windows.GridUnitType.Star);
            }
            return new System.Windows.GridLength(100, System.Windows.GridUnitType.Star);
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
