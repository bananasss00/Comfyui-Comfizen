using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Comfizen;

/// <summary>
/// A converter to determine the field color with priority: field color first, then group color.
/// </summary>
public class PriorityColorToBrushConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        // values[0] - field color, values[1] - group color
        string colorHex = null;

        if (values.Length > 0 && values[0] is string fieldColor && !string.IsNullOrEmpty(fieldColor))
        {
            colorHex = fieldColor;
        }
        else if (values.Length > 1 && values[1] is string groupColor && !string.IsNullOrEmpty(groupColor))
        {
            colorHex = groupColor;
        }

        if (colorHex != null)
        {
            try
            {
                // Create a brush with slight transparency for better blending with the background.
                var color = (Color)ColorConverter.ConvertFromString(colorHex);
                return new SolidColorBrush(color) { Opacity = 0.5 };
            }
            catch
            {
                // Return a transparent brush in case of an error.
                return Brushes.Transparent;
            }
        }
            
        // Default background if no color is specified.
        return Brushes.Transparent;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}