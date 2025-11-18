using System;
using PropertyChanged;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Newtonsoft.Json;

namespace Comfizen
{
    [AddINotifyPropertyChangedInterface]
    public class WorkflowGroupTab : INotifyPropertyChanged
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; }
        public ObservableCollection<WorkflowField> Fields { get; set; } = new();
        
        /// <summary>
        /// The highlight color for the tab in HEX format (e.g., #RRGGBB).
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string HighlightColor { get; set; }

        [JsonIgnore]
        public bool IsRenaming { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
    }
    
    [AddINotifyPropertyChangedInterface]
    public class WorkflowGroup : INotifyPropertyChanged
    {
        /// <summary>
        /// A unique, persistent identifier for this group, used to associate presets.
        /// </summary>
        public Guid Id { get; set; } = Guid.NewGuid();
        
        public string Name { get; set; }

        /// <summary>
        /// DEPRECATED: Use Tabs instead. This is kept for backward compatibility with older workflow files.
        /// It will be migrated to a default tab on load.
        /// </summary>
        [Obsolete]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public ObservableCollection<WorkflowField> Fields { get; set; } = new();

        /// <summary>
        /// This method tells Json.NET not to serialize the 'Fields' property
        /// if the new 'Tabs' property has any items. This is for forward compatibility.
        /// </summary>
        public bool ShouldSerializeFields()
        {
            return Tabs == null || !Tabs.Any();
        }

        /// <summary>
        /// A collection of tabs within this group, each holding a set of fields.
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public ObservableCollection<WorkflowGroupTab> Tabs { get; set; } = new();
        // --- КОНЕЦ ИЗМЕНЕНИЙ ---

        public bool IsRenaming { get; set; } = false;
        
        /// <summary>
        /// The highlight color for the group in HEX format (e.g., #RRGGBB).
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string HighlightColor { get; set; }

        /// <summary>
        /// Controls whether the group is expanded in the UI Constructor.
        /// This property is not saved to the workflow JSON file.
        /// </summary>
        [JsonIgnore]
        public bool IsExpandedInDesigner { get; set; } = true;

        /// <summary>
        /// Controls whether the group is expanded in the main UI.
        /// This property is saved to the session file.
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        [DefaultValue(true)]
        public bool IsExpanded { get; set; } = true;


        public event PropertyChangedEventHandler? PropertyChanged;
    }
}