using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace Comfizen;

public class FileNameTrimmerConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string fullPath)
        {
            return Path.ChangeExtension(fullPath, null);
        }
        return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // When a value is selected from the dropdown, add the .json extension back.
        if (value is string str && !string.IsNullOrEmpty(str))
        {
            // Ensure we don't add .json if it's already there.
            if (!str.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                return str + ".json";
            }
        }
        return value;
    }
}