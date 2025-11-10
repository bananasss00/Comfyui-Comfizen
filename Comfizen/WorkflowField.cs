// WorkflowField.cs

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using PropertyChanged;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
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
        
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        [DefaultValue("\\n")]
        public string Separator { get; set; } = "\\n";
        
        [JsonIgnore]
        public string ComboBoxItemsAsString
        {
            get
            {
                string sep;
                try
                {
                    // Attempt to unescape the separator (e.g., "\\n" becomes "\n").
                    sep = Regex.Unescape(Separator ?? "\\n");
                }
                catch (ArgumentException)
                {
                    // If Regex.Unescape fails (e.g., due to an invalid escape sequence like '\\т'),
                    // fall back to using the separator string literally.
                    sep = Separator ?? "\\n";
                }
                return string.Join(sep, ComboBoxItems);
            }
            set
            {
                string sep;
                try
                {
                    // Attempt to unescape the separator.
                    sep = Regex.Unescape(Separator ?? "\\n");
                }
                catch (ArgumentException)
                {
                    // Fallback to the literal string if unescaping fails.
                    sep = Separator ?? "\\n";
                }

                // When the separator is a newline, we need to handle both LF (\n) and CRLF (\r\n) line endings
                // common in text inputs, especially on Windows.
                if (sep == "\n")
                {
                    ComboBoxItems = value.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
                }
                else
                {
                    ComboBoxItems = value.Split(new[] { sep }, StringSplitOptions.None).ToList();
                }
                
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ComboBoxItems)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ComboBoxItemsAsString)));
            }
        }

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
        public int? MaxDisplayLines { get; set; } = 0;
        
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public ObservableCollection<string> BypassNodeIds { get; set; } = new ObservableCollection<string>();
        
        /// <summary>
        /// For WildcardSupportPrompt, enables an advanced token editor UI.
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        [DefaultValue(false)]
        public bool AdvancedPrompt { get; set; } = false;
        
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        [DefaultValue(SeparatorStyle.Solid)]
        public SeparatorStyle SeparatorStyle { get; set; } = SeparatorStyle.Solid;

        [JsonIgnore]
        public bool IsRenaming { get; set; } = false;
        
        [JsonIgnore]
        public bool IsInvalid { get; set; } = false;
        
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string NodeTitle { get; set; }
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string NodeType { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        
        // We call this manually after adding/removing items to force MultiBindings to update.
        public void NotifyBypassNodeIdsChanged()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BypassNodeIds)));
        }
    }
}