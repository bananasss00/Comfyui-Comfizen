// Workflow.cs

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

        public ObservableCollection<WorkflowGroup> Groups { get; set; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool IsLoaded => _loadedApi != null;

        public JToken Json() => _loadedApi;
        public JToken JsonClone() => _loadedApi.DeepClone();

        public void LoadApiWorkflow(string filePath)
        {
            var json = File.ReadAllText(filePath);
            LoadedApi = JObject.Parse(json);
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
                prompt = _loadedApi,
                promptTemplate = Groups
            };

            var jsonString = JsonConvert.SerializeObject(data, Formatting.Indented);

            // Собираем полный путь
            var fullPath = Path.Combine(WorkflowsDir, fileName + ".json");

            // Получаем путь к директории из полного пути к файлу
            var directoryPath = Path.GetDirectoryName(fullPath);

            // Создаем директорию, если она не существует. Это безопасно вызывать, даже если она есть.
            Directory.CreateDirectory(directoryPath);
    
            // Записываем файл
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
                        // <<====== ИЗМЕНЕНИЕ ЗДЕСЬ: Добавлена проверка на JTokenType.Boolean
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
            // БЫЛО:
            // var jsonString = File.ReadAllText(Path.Combine(WorkflowsDir, fileName));

            // СТАЛО:
            // Просто используем предоставленный fileName как есть, так как он уже является полным путем.
            var jsonString = File.ReadAllText(fileName);

            var data = JsonConvert.DeserializeAnonymousType(jsonString, new { prompt = default(JObject), promptTemplate = default(ObservableCollection<WorkflowGroup>) });

            LoadedApi = data.prompt;

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