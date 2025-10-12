using System;
using System.Globalization;
using System.Windows.Data;

namespace Comfizen;

/// <summary>
/// Converts multiple binding values into an object array.
/// Useful for passing multiple parameters to a Command.
/// </summary>
public class MultiValueToArrayConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        // Returns a clone of the array to prevent modification of internal WPF arrays
        return values?.Clone();
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}