using System;
using System.Globalization;
using System.Windows.Data;

namespace Comfizen;

/// <summary>
/// Converts an integer count to a boolean.
/// Returns true if the count equals the integer value passed in the ConverterParameter.
/// </summary>
public class SelectionCountToBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int count && int.TryParse(parameter?.ToString(), out int requiredCount))
        {
            return count == requiredCount;
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}