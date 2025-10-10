using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace Comfizen;

public class StringListToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is List<string> list)
        {
            return string.Join(", ", list);
        }
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string str)
        {
            // --- НАЧАЛО ИСПРАВЛЕНИЯ ---
            // Убираем StringSplitOptions.RemoveEmptyEntries, чтобы можно было вводить запятую
            return str.Split(new[] { ',' })
                .Select(s => s.Trim())
                .ToList();
            // --- КОНЕЦ ИСПРАВЛЕНИЯ ---
        }
        return new List<string>();
    }
}