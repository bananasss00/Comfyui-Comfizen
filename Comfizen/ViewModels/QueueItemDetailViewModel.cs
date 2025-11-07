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
        // START OF CHANGE: OriginalValue is no longer needed
        // public string OriginalValue { get; set; }
        // END OF CHANGE
        public string NewValue { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}