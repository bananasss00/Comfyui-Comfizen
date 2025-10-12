using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Comfizen;

/// <summary>
/// Converts a HEX color string to a SolidColorBrush.
/// </summary>
public class HexToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrEmpty(hex))
        {
            try
            {
                return (SolidColorBrush)new BrushConverter().ConvertFrom(hex);
            }
            catch
            {
                // Return null in case of an invalid format, allowing the FallbackValue to be used.
                return null;
            }
        }
        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}