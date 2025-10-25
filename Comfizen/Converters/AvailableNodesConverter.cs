using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace Comfizen;

/// <summary>
/// A converter that takes a full list of NodeInfo objects and a list of node IDs to exclude.
/// It returns a new list containing only the nodes that are not in the exclusion list.
/// </summary>
public class AvailableNodesConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        // Expecting [0] = All nodes (ObservableCollection<NodeInfo>), [1] = Excluded IDs (ObservableCollection<string>)
        if (values.Length < 2 ||
            values[0] is not IEnumerable<NodeInfo> allNodes ||
            values[1] is not ObservableCollection<string> excludedIds)
        {
            return null;
        }

        // Return a new list containing only the nodes whose IDs are not in the excluded list.
        return allNodes.Where(node => !excludedIds.Contains(node.Id)).ToList();
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}