// WorkflowField.cs
﻿using System.Collections.Generic;
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
        /// Список ID нод, которые будут обойдены, если это поле выключено.
        /// Используется только когда Type равен NodeBypass.
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public List<string> BypassNodeIds { get; set; } = new List<string>();

        [JsonIgnore]
        public bool IsRenaming { get; set; } = false;

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}