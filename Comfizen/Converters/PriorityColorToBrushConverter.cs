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
                // --- START OF FIX: Return a solid, non-transparent brush ---
                // The opacity was a workaround for the full background fill.
                // For a clean accent line, we need a solid color.
                var color = (Color)ColorConverter.ConvertFromString(colorHex);
                return new SolidColorBrush(color);
                // --- END OF FIX ---
            }
            catch
            {
                return Brushes.Transparent;
            }
        }
            
        return Brushes.Transparent;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}