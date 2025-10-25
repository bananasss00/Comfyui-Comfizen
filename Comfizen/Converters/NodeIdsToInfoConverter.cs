using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace Comfizen;

public class NodeIdsToInfoConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || 
            values[0] is not ObservableCollection<string> idList || 
            values[1] is not ObservableCollection<NodeInfo> allNodes)
        {
            return null;
        }

        // Находим объекты NodeInfo, чьи ID есть в списке, и возвращаем их
        return allNodes.Where(node => idList.Contains(node.Id)).ToList();
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}