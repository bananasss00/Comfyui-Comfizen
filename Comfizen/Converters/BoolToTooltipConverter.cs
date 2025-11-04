using System;
using System.Globalization;
using System.Windows.Data;

namespace Comfizen;

/// <summary>
/// A multi-value converter that returns one of two strings based on a boolean value.
/// Expects 3 values: [0] = boolean condition, [1] = string if true, [2] = string if false.
/// </summary>
public class BoolToStringConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values == null || values.Length < 3)
        {
            return string.Empty;
        }

        if (values[0] is bool condition)
        {
            return condition ? values[1] : values[2];
        }

        return string.Empty;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}