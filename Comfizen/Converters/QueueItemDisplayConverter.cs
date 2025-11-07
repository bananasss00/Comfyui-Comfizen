// QueueItemDisplayConverter.cs
using System;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;

namespace Comfizen
{
    public class AddOneConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int i)
            {
                return i + 1;
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    public class QueueItemDisplayConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // Если первый элемент не пришел или не того типа, возвращаем пустую строку
            if (values.Length < 1 || values[0] is not QueueItemViewModel item)
            {
                return "";
            }

            string name = item.WorkflowName ?? "Unknown";
            
            // Пробуем получить коллекцию элементов
            if (values.Length > 1 && values[1] is ItemCollection itemsCollection)
            {
                var index = itemsCollection.IndexOf(item);
                if (index != -1)
                {
                    return $"{index + 1}: {name}";
                }
            }

            // Если коллекцию не нашли или элемент не в ней, просто возвращаем знак вопроса
            return $"? {name}";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}