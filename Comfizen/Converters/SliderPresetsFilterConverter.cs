using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace Comfizen
{
    /// <summary>
    /// Filters the list of slider presets based on field type, search text, and a "Show All" toggle.
    /// </summary>
    public class SliderPresetsFilterConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // Expected values:
            // [0] IEnumerable<SliderDefaultRule> (All Rules)
            // [1] FieldType (Current field type: SliderInt or SliderFloat)
            // [2] string (Search Text)
            // [3] bool (Show All Types toggle)

            if (values.Length < 4 || values[0] is not IEnumerable<SliderDefaultRule> allRules)
            {
                return null;
            }

            var fieldType = values[1] is FieldType type ? type : FieldType.Any;
            var searchText = values[2] as string;
            var showAll = values[3] is bool b && b;

            // 1. Filter by Type
            IEnumerable<SliderDefaultRule> typeFiltered;

            if (showAll)
            {
                // FIX: If "Show All" is checked, return EVERYTHING.
                // Previously, we filtered SliderInt here, which prevented seeing float presets.
                typeFiltered = allRules;
            }
            else
            {
                // Strict filtering mode (default behavior)
                if (fieldType == FieldType.SliderInt)
                {
                    // Show ONLY Integer presets (whole numbers)
                    typeFiltered = allRules.Where(r => r.IsIntegerCompatible);
                }
                else if (fieldType == FieldType.SliderFloat)
                {
                    // Show ONLY Float presets (exclude strict integers to reduce clutter)
                    typeFiltered = allRules.Where(r => !r.IsIntegerCompatible);
                }
                else
                {
                    typeFiltered = allRules;
                }
            }

            // 2. Filter by Search Text (Multi-word)
            if (string.IsNullOrWhiteSpace(searchText))
            {
                return typeFiltered;
            }

            var searchTerms = searchText.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            return typeFiltered.Where(rule =>
            {
                return searchTerms.All(term =>
                    rule.DisplayName.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    rule.DisplayRange.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0
                );
            });
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}