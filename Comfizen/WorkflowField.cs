using System.Collections.Generic;
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
        
        public double? MinValue { get; set; }
        public double? MaxValue { get; set; }
        public double? StepValue { get; set; }
        public int? Precision { get; set; }
        
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string ModelType { get; set; }
        
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public List<string> ComboBoxItems { get; set; } = new List<string>();

        public bool IsRenaming { get; set; } = false;

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}