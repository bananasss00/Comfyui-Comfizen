using PropertyChanged;
using System.ComponentModel;

namespace Comfizen
{
    /// <summary>
    /// Represents a single row in the queue item details view, showing a changed parameter.
    /// </summary>
    [AddINotifyPropertyChangedInterface]
    public class QueueItemDetailViewModel : INotifyPropertyChanged
    {
        public string FieldPath { get; set; }
        public string DisplayName { get; set; }
        public string NewValue { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}