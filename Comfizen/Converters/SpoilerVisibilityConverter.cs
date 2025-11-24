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

            // Spoiler headers themselves are always visible.
            // We access the model through the ViewModel to check its type.
            if (currentFieldVM.FieldModel.Type == FieldType.Spoiler)
            {
                return Visibility.Visible;
            }

            int currentIndex = allFieldVMs.IndexOf(currentFieldVM);
            if (currentIndex == -1)
            {
                return Visibility.Visible;
            }

            // Look backwards from the current field to find the last spoiler header.
            for (int i = currentIndex - 1; i >= 0; i--)
            {
                var precedingFieldVM = allFieldVMs[i];
                if (precedingFieldVM.FieldModel.Type == FieldType.Spoiler)
                {
                    // If the spoiler we are under is collapsed, this field should be hidden.
                    // We check the state on the underlying model object.
                    return precedingFieldVM.FieldModel.IsSpoilerExpanded ? Visibility.Visible : Visibility.Collapsed;
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