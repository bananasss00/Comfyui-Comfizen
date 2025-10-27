﻿// WorkflowField.cs
﻿using System.Collections.Generic;
using System.Collections.ObjectModel;
using PropertyChanged;
using System.ComponentModel;
using Newtonsoft.Json;

namespace Comfizen
{
    [AddINotifyPropertyChangedInterface]
    public class WorkflowField : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public FieldType Type { get; set; } = FieldType.Any;
        
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool IsSeedLocked { get; set; } = false;
        
        /// <summary>
        /// The highlight color for the field in HEX format (e.g., #RRGGBB).
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string HighlightColor { get; set; }
        
        public double? MinValue { get; set; }
        public double? MaxValue { get; set; }
        public double? StepValue { get; set; }
        public int? Precision { get; set; }
        
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string ModelType { get; set; }
        
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public List<string> ComboBoxItems { get; set; } = new List<string>();

        // --- START OF CHANGES ---
        /// <summary>
        /// The name of the script action to execute when this button is clicked.
        /// Used only when Type is ScriptButton.
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string ActionName { get; set; }
        // --- END OF CHANGES ---
        
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string DefaultValue { get; set; } = string.Empty;

        /// <summary>
        /// For Markdown fields, specifies the maximum number of lines to display before a scrollbar appears.
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int? MaxDisplayLines { get; set; } = 10;
        
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public ObservableCollection<string> BypassNodeIds { get; set; } = new ObservableCollection<string>();
        
        /// <summary>
        /// For WildcardSupportPrompt, enables an advanced token editor UI.
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        [DefaultValue(false)]
        public bool AdvancedPrompt { get; set; } = false;

        [JsonIgnore]
        public bool IsRenaming { get; set; } = false;

        public event PropertyChangedEventHandler? PropertyChanged;
        
        // We call this manually after adding/removing items to force MultiBindings to update.
        public void NotifyBypassNodeIdsChanged()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BypassNodeIds)));
        }
    }
}