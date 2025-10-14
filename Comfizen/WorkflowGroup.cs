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

        // --- START OF CHANGES ---
        /// <summary>
        /// Controls whether the group is expanded in the UI Constructor.
        /// This property is not saved to the workflow JSON file.
        /// </summary>
        [JsonIgnore]
        public bool IsExpandedInDesigner { get; set; } = true;
        // --- END OF CHANGES ---

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}