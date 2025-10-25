using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PropertyChanged;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace Comfizen
{
    [AddINotifyPropertyChangedInterface]
    public class WorkflowTabDefinition : INotifyPropertyChanged
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; }
        public ObservableCollection<Guid> GroupIds { get; set; } = new ObservableCollection<Guid>();

        [JsonIgnore]
        public bool IsRenaming { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
    }
    
    // Add a new class to represent a single preset.
    // This class will be serialized into the workflow file.
    public class GroupPreset
    {
        /// <summary>
        /// The user-visible name of the preset.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// A dictionary storing the state of the widgets.
        /// Key: The unique 'Path' of the WorkflowField.
        /// Value: The JToken representing the value of the field.
        /// </summary>
        public Dictionary<string, JToken> Values { get; set; } = new Dictionary<string, JToken>();
    }
    
    public class ScriptCollection
    {
        public Dictionary<string, string> Hooks { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> Actions { get; set; } = new Dictionary<string, string>();
    }
    
    [AddINotifyPropertyChangedInterface]
    public class Workflow : INotifyPropertyChanged
    {
        public const string WorkflowsDir = "workflows";

        private JObject? _loadedApi;

        /// <summary>
        /// The live state of the workflow, including any user-modified widget values.
        /// </summary>
        public JObject? LoadedApi
        {
            get => _loadedApi;
            set
            {
                if (_loadedApi != value)
                {
                    _loadedApi = value;
                }
            }
        }
        
        /// <summary>
        /// The original, unmodified API definition from the workflow file. Used for fingerprinting.
        /// </summary>
        public JObject OriginalApi { get; private set; }

        // --- START OF NEW PROPERTY ---
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public ObservableCollection<WorkflowTabDefinition> Tabs { get; set; } = new ObservableCollection<WorkflowTabDefinition>();
        // --- END OF NEW PROPERTY ---
        public ObservableCollection<WorkflowGroup> Groups { get; set; } = new();
        
        public ScriptCollection Scripts { get; set; } = new ScriptCollection();
        [JsonIgnore]
        public HashSet<string> BlockedNodeIds { get; set; } = new HashSet<string>();
        
        /// <summary>
        /// Stores presets for widget groups.
        /// Key: The unique ID of the WorkflowGroup.
        /// Value: A list of named presets for that group.
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public Dictionary<Guid, List<GroupPreset>> Presets { get; set; } = new Dictionary<Guid, List<GroupPreset>>();
        
        public event PropertyChangedEventHandler? PropertyChanged;

        public bool IsLoaded => _loadedApi != null;

        public JToken Json() => _loadedApi;
        public JObject? JsonClone() => _loadedApi?.DeepClone() as JObject;
        
        /// <summary>
        /// Creates a deep copy of the entire Workflow object.
        /// This is achieved by serializing the current instance to JSON and deserializing it into a new object.
        /// This ensures that the clone is completely independent of the original.
        /// </summary>
        /// <returns>A new, deep-cloned Workflow instance.</returns>
        public Workflow Clone()
        {
            // Serialize the current object to a JSON string.
            var serialized = JsonConvert.SerializeObject(this);
            // Deserialize the string back into a new Workflow object.
            var cloned = JsonConvert.DeserializeObject<Workflow>(serialized);
            return cloned;
        }
        
        // New public method to initialize a workflow object from parts.
        public void SetWorkflowData(JObject prompt, ObservableCollection<WorkflowGroup> promptTemplate, ScriptCollection scripts, ObservableCollection<WorkflowTabDefinition> tabs)
        {
            OriginalApi = prompt;
            LoadedApi = prompt?.DeepClone() as JObject;

            // START OF FIX: Restore original inputs immediately after loading API state
            RestoreBypassedNodesFromMeta(LoadedApi);
            // END OF FIX

            Groups.Clear();
            if (promptTemplate != null) { foreach (var group in promptTemplate) Groups.Add(group); }

            Scripts = scripts ?? new ScriptCollection();
            Tabs = tabs ?? new ObservableCollection<WorkflowTabDefinition>();
            
            // Run the migration logic here as well to handle older imported formats.
            if (LoadedApi != null)
            {
                foreach (var group in Groups)
                {
                    foreach (var field in group.Fields)
                    {
                        if (field.Type == FieldType.Markdown && string.IsNullOrEmpty(field.DefaultValue))
                        {
                            var prop = Utils.GetJsonPropertyByPath(LoadedApi, field.Path);
                            if (prop != null && prop.Value.Type == JTokenType.String)
                            {
                                field.DefaultValue = prop.Value.ToString();
                                prop.Value = "";
                            }
                        }
                    }
                }
            }
        }
        
        public void LoadApiWorkflow(string filePath)
        {
            var json = File.ReadAllText(filePath);
            var parsedJson = JObject.Parse(json);
            OriginalApi = parsedJson;
            LoadedApi = parsedJson.DeepClone() as JObject; // The live version is a clone
        }

        public JProperty? GetPropertyByPath(string path)
        {
            return Utils.GetJsonPropertyByPath(_loadedApi, path);
        }

        public void AddFieldToGroup(string groupName, WorkflowField field)
        {
            var group = Groups.FirstOrDefault(x => x.Name == groupName);
            if (group == null)
            {
                group = new WorkflowGroup { Name = groupName };
                Groups.Add(group);
            }

            if (!group.Fields.Any(f => f.Path == field.Path))
            {
                group.Fields.Add(field);
            }
        }

        public void MoveFieldToGroup(WorkflowField field, WorkflowGroup sourceGroup, WorkflowGroup targetGroup)
        {
            if (sourceGroup != null && targetGroup != null && field != null)
            {
                sourceGroup.Fields.Remove(field);
                targetGroup.Fields.Add(field);
            }
        }

        public void SaveWorkflow(string fileName)
        {
            var data = new
            {
                prompt = OriginalApi,
                promptTemplate = Groups,
                scripts = (Scripts.Hooks.Any() || Scripts.Actions.Any()) ? Scripts : null,
                presets = Presets.Any() ? Presets : null,
                // --- ADDED: Save tabs information ---
                tabs = Tabs.Any() ? Tabs : null 
            };

            var jsonString = JsonConvert.SerializeObject(data, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore, Formatting = Formatting.Indented });
            var fullPath = Path.Combine(WorkflowsDir, fileName + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, jsonString);
        }
        
        public void SaveWorkflowWithCurrentState(string fileName)
        {
            var data = new
            {
                prompt = LoadedApi,
                promptTemplate = Groups,
                scripts = (Scripts.Hooks.Any() || Scripts.Actions.Any()) ? Scripts : null,
                presets = Presets.Any() ? Presets : null,
                // --- START OF CHANGE: Add tabs serialization ---
                tabs = Tabs.Any() ? Tabs : null
                // --- END OF CHANGE ---
            };

            var jsonString = JsonConvert.SerializeObject(data, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore, Formatting = Formatting.Indented });
            var fullPath = Path.Combine(WorkflowsDir, fileName + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, jsonString);
        }

        public List<WorkflowField> ParseFields()
        {
            if (_loadedApi == null)
                return new();

            List<WorkflowField> inputs = new();

            foreach (var property in _loadedApi.Properties())
            {
                var title = (string)property.Value["_meta"]?["title"] ?? property.Name;
                var inputsObj = property.Value["inputs"];

                if (inputsObj is JObject inputsDict)
                {
                    inputs.AddRange(inputsDict.Properties()
                        .Where(input => input.Value.Type == JTokenType.String ||
                                        input.Value.Type == JTokenType.Integer ||
                                        input.Value.Type == JTokenType.Float ||
                                        input.Value.Type == JTokenType.Boolean
                        )
                        .Select(input => new WorkflowField()
                        {
                            Name = $"{title}::{input.Name}",
                            Path = input.Path
                        }));
                }
            }

            return inputs;
        }

        public void LoadWorkflow(string fileName)
        {
            var jsonString = File.ReadAllText(fileName);
            var data = JsonConvert.DeserializeAnonymousType(jsonString, new { 
                prompt = default(JObject), 
                promptTemplate = default(ObservableCollection<WorkflowGroup>),
                scripts = default(ScriptCollection),
                presets = default(Dictionary<Guid, List<GroupPreset>>),
                tabs = default(ObservableCollection<WorkflowTabDefinition>)
            });

            OriginalApi = data.prompt;
            LoadedApi = data.prompt?.DeepClone() as JObject;

            // START OF FIX: Restore original inputs immediately after loading API state
            RestoreBypassedNodesFromMeta(LoadedApi);
            // END OF FIX

            Groups.Clear();
            if (data.promptTemplate != null) { foreach (var group in data.promptTemplate) Groups.Add(group); }
            
            Tabs = data.tabs ?? new ObservableCollection<WorkflowTabDefinition>();
            Scripts = data.scripts ?? new ScriptCollection();
            Presets = data.presets ?? new Dictionary<Guid, List<GroupPreset>>();

            // --- НАЧАЛО МИГРАЦИИ ДЛЯ ОБРАТНОЙ СОВМЕСТИМОСТИ ---
            if (LoadedApi != null)
            {
                foreach (var group in Groups)
                {
                    foreach (var field in group.Fields)
                    {
                        // Мигрируем только Markdown-поля, у которых еще нет значения в DefaultValue
                        if (field.Type == FieldType.Markdown && string.IsNullOrEmpty(field.DefaultValue))
                        {
                            // Пытаемся найти свойство в API по старому пути
                            var prop = Utils.GetJsonPropertyByPath(LoadedApi, field.Path);
                            if (prop != null && prop.Value.Type == JTokenType.String)
                            {
                                // Копируем значение из API в новое поле DefaultValue
                                field.DefaultValue = prop.Value.ToString();
                        
                                // Очищаем старое значение в API, чтобы завершить миграцию
                                prop.Value = ""; 
                            }
                        }
                    }
                }
            }
            // --- КОНЕЦ МИГРАЦИИ ---
        }
        
        /// <summary>
        /// Iterates through all nodes in the provided JObject and restores their 'inputs'
        /// from the '_meta.original_inputs' backup if it exists. This ensures the workflow
        /// is always in its original, un-bypassed state after loading.
        /// </summary>
        /// <param name="prompt">The JObject representing the workflow API.</param>
        private void RestoreBypassedNodesFromMeta(JObject prompt)
        {
            if (prompt == null) return;

            foreach (var nodeProperty in prompt.Properties())
            {
                if (nodeProperty.Value is not JObject node || 
                    node["_meta"]?["original_inputs"] is not JObject originalInputs)
                {
                    continue;
                }

                // If a backup of original inputs exists, restore it.
                // This makes it the canonical state for the loaded workflow.
                node["inputs"] = originalInputs.DeepClone();
            }
        }
    }
}
