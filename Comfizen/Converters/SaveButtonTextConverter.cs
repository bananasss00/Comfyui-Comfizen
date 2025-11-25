// SaveButtonTextConverter.cs

using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace Comfizen
{
    /// <summary>
    /// Creates display text for save buttons.
    /// Now simply returns the fallback text, as the format is no longer shown on the main button.
    /// The logic to handle videos vs images is now controlled by the visibility of the dropdown toggle.
    /// </summary>
    public class SaveButtonTextConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // values[0] = formatString (e.g. "Save") - now the main text
            // values[1] = formatEnum (e.g. ImageSaveFormat.Png) - ignored
            // values[2] = isAnyVideoSelected (bool) - ignored
            // values[3] = fallbackText (e.g. "Save") - redundant but kept for binding structure
            if (values.Length < 4 || values.Any(v => v == DependencyProperty.UnsetValue))
            {
                return Binding.DoNothing;
            }

            // The main button text is now determined by the first binding, which is the localization key.
            // We no longer need to check for video type here, as the button text is always generic.
            var mainButtonText = values[0] as string;
            
            return mainButtonText;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}