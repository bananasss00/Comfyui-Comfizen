using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace Comfizen
{
    public class SpoilerVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // values[0] is the current InputFieldViewModel item.
            // values[1] is the entire collection of InputFieldViewModel in the current list.
            if (values.Length < 2 || 
                !(values[0] is InputFieldViewModel currentFieldVM) || 
                !(values[1] is ObservableCollection<InputFieldViewModel> allFieldVMs))
            {
                return Visibility.Visible;
            }

            // Spoiler headers and SpoilerEnd markers are always visible.
            if (currentFieldVM.FieldModel.Type == FieldType.Spoiler || currentFieldVM.FieldModel.Type == FieldType.SpoilerEnd)
            {
                return Visibility.Visible;
            }

            int currentIndex = allFieldVMs.IndexOf(currentFieldVM);
            if (currentIndex == -1)
            {
                return Visibility.Visible;
            }
            
            // Look backwards from the current field.
            for (int i = currentIndex - 1; i >= 0; i--)
            {
                var precedingFieldModel = allFieldVMs[i];
        
                // If we find an end marker before a start marker, this field is not in a spoiler.
                if (precedingFieldModel.Type == FieldType.SpoilerEnd)
                {
                    return Visibility.Visible;
                }

                // If we find the start of a spoiler.
                if (precedingFieldModel.Type == FieldType.Spoiler)
                {
                    // If the spoiler is collapsed, hide this field. Otherwise, show it.
                    return precedingFieldModel.FieldModel.IsSpoilerExpanded ? Visibility.Visible : Visibility.Collapsed;
                }
            }

            // If no spoiler was found before this field, it's visible by default.
            return Visibility.Visible;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}