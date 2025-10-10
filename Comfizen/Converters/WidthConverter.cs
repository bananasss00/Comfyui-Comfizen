using System;
using System.Globalization;
using System.Windows.Data;

namespace Comfizen;

public class ElementWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        double width = (double)values[0];
        return width;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}