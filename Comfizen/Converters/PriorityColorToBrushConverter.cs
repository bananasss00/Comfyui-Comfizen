using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Comfizen;

/// <summary>
/// A converter that takes a potential color hex string and a fallback brush.
/// If the hex string is a valid color, it returns a brush for that color.
/// Otherwise, it returns the fallback brush.
/// </summary>
public class PriorityColorToBrushConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        // Expected: values[0] is the HEX string (e.g., from HighlightColor)
        //           values[1] is the fallback Brush (e.g., {StaticResource SecondaryBackground})
        if (values.Length > 0 && values[0] is string hexColor && !string.IsNullOrEmpty(hexColor))
        {
            try
            {
                // If the first value is a valid hex string, use it.
                return (SolidColorBrush)new BrushConverter().ConvertFrom(hexColor);
            }
            catch
            {
                // If conversion fails, proceed to the fallback.
            }
        }

        // If the first value was invalid or not present, use the second value as the fallback.
        if (values.Length > 1 && values[1] is Brush fallbackBrush)
        {
            return fallbackBrush;
        }
            
        // Default fallback if nothing works.
        return Brushes.Transparent;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}