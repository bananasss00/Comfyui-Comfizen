using System.Windows;
using System.Windows.Controls;

namespace Comfizen
{
    // A simple template selector for the main workflow dropdown.
    public class WorkflowTemplateSelector : DataTemplateSelector
    {
        // Template for regular items (strings)
        public DataTemplate WorkflowTemplate { get; set; }

        // Template for header items
        public DataTemplate HeaderTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            if (item is WorkflowListHeader)
            {
                return HeaderTemplate;
            }
            return WorkflowTemplate;
        }
    }
}