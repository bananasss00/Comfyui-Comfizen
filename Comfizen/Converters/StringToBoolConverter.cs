using System;
using System.Globalization;
using System.Windows.Data;

namespace Comfizen;

public class StringToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string s)
        {
            return s.ToLower() == "true";
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return b.ToString();
        }
        return "false";
    }
}