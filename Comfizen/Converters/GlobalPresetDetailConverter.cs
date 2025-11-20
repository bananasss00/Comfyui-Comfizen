using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace Comfizen
{
    // Конвертер для генерации данных тултипа/попапа на лету
    public class GlobalPresetDetailConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // values[0] = GlobalPreset
            // values[1] = GlobalControlsViewModel
            
            if (values.Length < 2 || 
                values[0] is not GlobalPreset preset || 
                values[1] is not GlobalControlsViewModel vm)
            {
                return null;
            }

            // Вызываем публичный метод VM для генерации данных
            return vm.GetTooltipDataForPreset(preset);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}