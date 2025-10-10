using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace Comfizen
{
    /// <summary>
    /// A converter that formats a string using arguments passed via a MultiBinding.
    /// The first value in the binding is expected to be the format string (e.g., "Value: {0}").
    /// Subsequent values are used as arguments for the format string.
    /// </summary>
    public class MultiBindingStringFormatConverter : IMultiValueConverter
    {
        /// <summary>
        /// Formats the string.
        /// </summary>
        /// <param name="values">The array of values from the MultiBinding. values[0] should be the format string.</param>
        /// <param name="targetType">The type of the binding target property.</param>
        /// <param name="parameter">The converter parameter to use.</param>
        /// <param name="culture">The culture to use in the converter.</param>
        /// <returns>A formatted string, or an empty string if formatting fails.</returns>
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // Check if the first value is a valid format string.
            if (values == null || values.Length < 1 || values[0] is not string format)
            {
                return string.Empty;
            }

            // Get the arguments for the format string.
            var args = values.Skip(1).ToArray();
            
            try
            {
                // Return the formatted string.
                return string.Format(culture, format, args);
            }
            catch (FormatException)
            {
                // If formatting fails (e.g., wrong number of arguments), return the raw format string.
                return format;
            }
        }

        /// <summary>
        /// This method is not supported and will throw an exception.
        /// </summary>
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}