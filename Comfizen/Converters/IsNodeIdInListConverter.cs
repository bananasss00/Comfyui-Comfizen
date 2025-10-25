using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;

namespace Comfizen;

public class IsNodeIdInListConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 2 && values[0] is string nodeId && values[1] is List<string> idList)
        {
            return idList.Contains(nodeId);
        }
        return false;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}