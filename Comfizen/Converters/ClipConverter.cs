using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Comfizen;

public class ClipConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || !(values[0] is double sliderValue) || !(values[1] is double actualWidth))
        {
            return null;
        }

        // sliderValue is from 0 to 100
        double clipX = (sliderValue / 100.0) * actualWidth;

        return new RectangleGeometry(new Rect(clipX, 0, actualWidth - clipX, 9999)); // Height is large to not clip vertically
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}