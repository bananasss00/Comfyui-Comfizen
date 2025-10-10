using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Comfizen
{
    public class BoolToGridLengthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isVisible && isVisible)
            {
                // Если true, возвращаем значение из параметра или 'Auto' по умолчанию
                string param = parameter as string ?? "Auto";
                return (GridLength)new GridLengthConverter().ConvertFromString(param);
            }
            // Если false, полностью схлопываем строку
            return new GridLength(0);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
