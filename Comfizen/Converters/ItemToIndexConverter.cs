using System;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;

namespace Comfizen;

/// <summary>
/// Converts a ListBoxItem to its index within the ListBox, adding 1 to start numbering from 1.
/// </summary>
public class ItemToIndexConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ListBoxItem item)
        {
            var listBox = ItemsControl.ItemsControlFromItemContainer(item) as ListBox;
            if (listBox != null)
            {
                int index = listBox.ItemContainerGenerator.IndexFromContainer(item);
                return (index + 1).ToString();
            }
        }
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}