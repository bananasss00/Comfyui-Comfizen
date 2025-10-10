using System.Globalization;
using System;
using System.Windows.Data;
using System.Windows.Media;

namespace Comfizen
{
    public class AlternationConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int index = (int)value % 2; // Можно изменить количество цветов
            return index == 0 ? Brushes.White : Brushes.LightGray; // Выберите нужные цвета
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
