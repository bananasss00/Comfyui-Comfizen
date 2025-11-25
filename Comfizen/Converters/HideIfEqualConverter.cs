// HideIfEqualConverter.cs

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Comfizen
{
    /// <summary>
    /// Converts a value to Visibility.Collapsed if it equals the parameter.
    /// Otherwise, returns Visibility.Visible.
    /// </summary>
    public class HideIfEqualConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value != null && value.Equals(parameter))
            {
                return Visibility.Collapsed;
            }
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}