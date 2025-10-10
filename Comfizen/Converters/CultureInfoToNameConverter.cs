using System;
using System.Globalization;
using System.Windows.Data;

namespace Comfizen
{
    /// <summary>
    /// Converts between a CultureInfo object and its string name (e.g., "en-US").
    /// This is used for binding a ComboBox of CultureInfo objects to a string setting.
    /// </summary>
    public class CultureInfoToNameConverter : IValueConverter
    {
        /// <summary>
        /// Converts a language code string to a CultureInfo object.
        /// </summary>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string langCode)
            {
                try
                {
                    return new CultureInfo(langCode);
                }
                catch
                {
                    return null;
                }
            }
            return null;
        }

        /// <summary>
        /// Converts a CultureInfo object back to its language code string.
        /// </summary>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is CultureInfo ci)
            {
                return ci.Name;
            }
            return null;
        }
    }
}