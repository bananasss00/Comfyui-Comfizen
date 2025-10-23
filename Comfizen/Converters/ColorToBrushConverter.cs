using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Brushes = System.Drawing.Brushes;
using Color = System.Windows.Media.Color;

namespace Comfizen;

/// <summary>
/// Converts a Color or nullable Color object to a SolidColorBrush.
/// </summary>
public class ColorToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // First, check for a nullable Color
        if (value is Color?) 
        {
            var nullableColor = (Color?)value;
            if (nullableColor.HasValue)
            {
                return new SolidColorBrush(nullableColor.Value);
            }
        }
        // Then, check for a non-nullable Color
        else if (value is Color color)
        {
            return new SolidColorBrush(color);
        }

        // If it's neither or a null Color?, return Transparent
        return Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}