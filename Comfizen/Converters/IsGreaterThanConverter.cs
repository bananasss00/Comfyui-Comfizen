using System;
using System.Globalization;
using System.Windows.Data;

namespace Comfizen;

public class IsGreaterThanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int intValue && parameter is string stringParam && int.TryParse(stringParam, out int compareValue))
        {
            return intValue > compareValue;
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}