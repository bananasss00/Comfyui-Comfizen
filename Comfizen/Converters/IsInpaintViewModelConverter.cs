using System;
using System.Globalization;
using System.Windows.Data;

namespace Comfizen
{
    public class IsInpaintViewModelConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is InpaintFieldViewModel;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}