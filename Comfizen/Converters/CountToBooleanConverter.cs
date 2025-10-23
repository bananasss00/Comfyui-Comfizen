using System;
using System.Globalization;
using System.Windows.Data;

namespace Comfizen;

/// <summary>
/// Converts an integer count to a boolean value.
/// Returns true if the count is greater than 0, otherwise false.
/// </summary>
public class CountToBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int count)
        {
            // Returns true if count > 0, otherwise false
            return count > 0;
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}