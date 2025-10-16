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


        public ObservableCollection<WorkflowGroup> Groups { get; set; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool IsLoaded => _loadedApi != null;

        public JToken Json() => _loadedApi;
        public JToken JsonClone() => _loadedApi.DeepClone();

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
                promptTemplate = Groups
            };

            var jsonString = JsonConvert.SerializeObject(data, Formatting.Indented);

            var fullPath = Path.Combine(WorkflowsDir, fileName + ".json");
            var directoryPath = Path.GetDirectoryName(fullPath);
            Directory.CreateDirectory(directoryPath);
            File.WriteAllText(fullPath, jsonString);
        }
        
        public void SaveWorkflowWithCurrentState(string fileName)
        {
            var data = new
            {
                prompt = LoadedApi,
                promptTemplate = Groups
            };

            var jsonString = JsonConvert.SerializeObject(data, Formatting.Indented);

            var fullPath = Path.Combine(WorkflowsDir, fileName + ".json");
            var directoryPath = Path.GetDirectoryName(fullPath);
            Directory.CreateDirectory(directoryPath);
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

            var data = JsonConvert.DeserializeAnonymousType(jsonString, new { prompt = default(JObject), promptTemplate = default(ObservableCollection<WorkflowGroup>) });

            OriginalApi = data.prompt;
            LoadedApi = data.prompt?.DeepClone() as JObject;

            Groups.Clear();
            if (data.promptTemplate != null)
            {
                foreach (var group in data.promptTemplate)
                {
                    Groups.Add(group);
                }
            }
        }
    }
}
