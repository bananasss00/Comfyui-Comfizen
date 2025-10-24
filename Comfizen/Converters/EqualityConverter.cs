using System;
using System.Globalization;
using System.Windows.Data;

namespace Comfizen;

/// <summary>
/// A multi-value converter that returns true if all items in the values array are equal.
/// </summary>
public class EqualityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        // If there are less than two values, they can't be compared.
        if (values == null || values.Length < 2)
        {
            return false;
        }

        // Compare the first item with all subsequent items.
        for (int i = 1; i < values.Length; i++)
        {
            if (values[0] != values[i])
            {
                return false;
            }
        }

        return true;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}