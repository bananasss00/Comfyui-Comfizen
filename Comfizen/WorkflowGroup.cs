using PropertyChanged;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Newtonsoft.Json;

namespace Comfizen
{
    [AddINotifyPropertyChangedInterface]
    public class WorkflowGroup : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public ObservableCollection<WorkflowField> Fields { get; set; } = new();

        public bool IsRenaming { get; set; } = false;
        
        /// <summary>
        /// The highlight color for the group in HEX format (e.g., #RRGGBB).
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string HighlightColor { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}