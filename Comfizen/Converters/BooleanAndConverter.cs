// BooleanAndConverter.cs

using System;
using System.Globalization;
using System.Windows.Data;

namespace Comfizen
{
    public class BooleanAndConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // values[0] is IsPresetPanelOpen (bool)
            // values[1] is ToggleButton.IsVisible (bool)
            
            if (values.Length >= 2 && values[0] is bool isOpen && values[1] is bool isVisible)
            {
                // Открываем попап только если флаг в VM истина И кнопка видна
                return isOpen && isVisible;
            }
            return false;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            // Когда попап закрывается (value = false), мы должны записать это обратно в VM.
            // Binding.DoNothing для второго значения означает, что мы не пытаемся менять свойство IsVisible.
            if (value is bool boolValue)
            {
                return new object[] { boolValue, Binding.DoNothing };
            }
            return new object[] { false, Binding.DoNothing };
        }
    }
}