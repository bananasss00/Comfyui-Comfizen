using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Comfizen;

public class ExpanderCornerRadiusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isExpanded && isExpanded)
        {
            return new CornerRadius(3, 3, 0, 0);
        }
        return new CornerRadius(3);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}