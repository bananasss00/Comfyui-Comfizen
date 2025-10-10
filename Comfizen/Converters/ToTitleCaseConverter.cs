using System;
using System.Globalization;
using System.Windows.Data;

namespace Comfizen
{
    /// <summary>
    /// Converts a string to Title Case (e.g., "hello world" becomes "Hello World").
    /// </summary>
    public class ToTitleCaseConverter : IValueConverter
    {
        /// <summary>
        /// Converts the string to Title Case.
        /// </summary>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string str && !string.IsNullOrEmpty(str))
            {
                // We use the current UI culture's TextInfo to apply the correct casing rules.
                return CultureInfo.CurrentUICulture.TextInfo.ToTitleCase(str);
            }
            return value;
        }

        /// <summary>
        /// This method is not supported.
        /// </summary>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}