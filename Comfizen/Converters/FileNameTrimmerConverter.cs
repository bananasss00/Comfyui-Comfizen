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
            // Используем Path.GetFileNameWithoutExtension для надежности,
            // он корректно обработает и "file.json", и "folder/file.json"
            return Path.ChangeExtension(fullPath, null);
        }
        return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Обратное преобразование не требуется
        return value;
    }
}