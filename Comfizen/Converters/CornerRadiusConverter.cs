using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Comfizen;

/// <summary>
/// Converts a string tag ("First", "Last") into a CornerRadius for segmented controls.
/// </summary>
public class CornerRadiusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        switch (value?.ToString())
        {
            case "First":
                return new CornerRadius(3, 0, 0, 3);
            case "Last":
                return new CornerRadius(0, 3, 3, 0);
            default:
                return new CornerRadius(0);
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}