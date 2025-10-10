using PropertyChanged;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Comfizen
{
    [AddINotifyPropertyChangedInterface]
    public class WorkflowGroup : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public ObservableCollection<WorkflowField> Fields { get; set; } = new();

        public bool IsRenaming { get; set; } = false;

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}