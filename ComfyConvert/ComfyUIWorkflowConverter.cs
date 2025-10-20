using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace ComfyUIConverter
{
    /// <summary>
    /// Универсальный конвертер рабочих процессов ComfyUI из полного формата в формат API.
    /// Динамически запрашивает и анализирует информацию о нодах, включая гибкие типы данных, 
    /// для корректного сопоставления виджетов и отсеивания "виртуальных" UI-значений.
    /// </summary>
    public class ComfyUIWorkflowConverter
    {
        // --- Публичный интерфейс ---

        public static async Task<ComfyUIWorkflowConverter> CreateAsync(string comfyUiUrl = "http://127.0.0.1:8188")
        {
            var apiUrl = $"{comfyUiUrl.TrimEnd('/')}/object_info";
            try
            {
                var response = await _httpClient.GetAsync(apiUrl);
                response.EnsureSuccessStatusCode();
                string objectInfoJson = await response.Content.ReadAsStringAsync();
                return new ComfyUIWorkflowConverter(objectInfoJson);
            }
            catch (HttpRequestException ex)
            {
                throw new InvalidOperationException($"Не удалось получить информацию о нодах с '{apiUrl}'. Убедитесь, что ComfyUI запущен и доступен.", ex);
            }
        }

        public string ConvertWorkflow(string fullWorkflowJson)
        {
            var fullWorkflow = JsonConvert.DeserializeObject<FullWorkflow>(fullWorkflowJson);
            if (fullWorkflow == null || fullWorkflow.Nodes == null)
            {
                throw new ArgumentException("Неверный или пустой JSON рабочего процесса.", nameof(fullWorkflowJson));
            }

            var apiWorkflow = new Dictionary<string, ApiNode>();
            var linksMap = fullWorkflow.Links.ToDictionary(link => Convert.ToInt64(link[0]));

            foreach (var node in fullWorkflow.Nodes)
            {
                if (node.Type == "MarkdownNote") continue;

                var apiNode = new ApiNode
                {
                    ClassType = node.Type,
                    Meta = new Meta { Title = node.Title ?? node.Type },
                    Inputs = new Dictionary<string, object>()
                };
                
                var linkedInputNames = new HashSet<string>();

                if (node.Inputs != null)
                {
                    foreach (var input in node.Inputs.Where(i => i.Link != null))
                    {
                        if (linksMap.TryGetValue(input.Link.Value, out var linkDetails))
                        {
                            string sourceNodeId = linkDetails[1].ToString();
                            int sourceSlot = Convert.ToInt32(linkDetails[2]);
                            apiNode.Inputs[input.Name] = new object[] { sourceNodeId, sourceSlot };
                            linkedInputNames.Add(input.Name);
                        }
                    }
                }
                
                MapWidgetValuesToInputs(node, apiNode, linkedInputNames);
                apiWorkflow.Add(node.Id.ToString(), apiNode);
            }

            return JsonConvert.SerializeObject(apiWorkflow, Formatting.Indented);
        }
        
        // --- Приватная реализация ---

        private readonly Dictionary<string, ObjectInfoNode> _objectInfo;
        private static readonly HttpClient _httpClient = new HttpClient();

        private ComfyUIWorkflowConverter(string objectInfoJson)
        {
            _objectInfo = JsonConvert.DeserializeObject<Dictionary<string, ObjectInfoNode>>(objectInfoJson);
        }

        private void MapWidgetValuesToInputs(Node node, ApiNode apiNode, ISet<string> linkedInputNames)
        {
            if (node.WidgetsValues == null || !node.WidgetsValues.Any()) return;

            if (!_objectInfo.TryGetValue(node.Type, out var nodeInfo) || nodeInfo.InputOrder?.Required == null || nodeInfo.Input?.Required == null)
            {
                Console.WriteLine($"Предупреждение: Для типа узла '{node.Type}' не найдено полной информации о входах. Виджеты будут пропущены.");
                return;
            }

            var widgetNamesInOrder = nodeInfo.InputOrder.Required
                                           .Where(inputName => !linkedInputNames.Contains(inputName))
                                           .ToList();
            
            var widgetValuesQueue = new Queue<object>(node.WidgetsValues);

            foreach (var widgetName in widgetNamesInOrder)
            {
                if (!nodeInfo.Input.Required.TryGetValue(widgetName, out var typeDefinitionToken))
                {
                    continue; // Не удалось найти информацию о типе для этого входа
                }

                // Входы, которые не являются виджетами (например, "trigger": "TRIGGER"), не должны потреблять значения.
                if (!IsWidgetType(typeDefinitionToken))
                {
                    continue;
                }

                while (widgetValuesQueue.Any())
                {
                    var currentValue = JToken.FromObject(widgetValuesQueue.Peek());
                    
                    if (IsTypeMatch(currentValue, typeDefinitionToken))
                    {
                        apiNode.Inputs[widgetName] = widgetValuesQueue.Dequeue();
                        break; 
                    }
                    else
                    {
                        widgetValuesQueue.Dequeue();
                    }
                }
            }
        }

        private bool IsWidgetType(JToken typeDefinition)
        {
            // Виджеты всегда определяются массивом, например ["INT", {...}] или [["a", "b"], {...}]
            return typeDefinition is JArray;
        }

        private bool IsTypeMatch(JToken value, JToken expectedTypeDefinition)
        {
            if (!(expectedTypeDefinition is JArray expectedTypeArray) || expectedTypeArray.Count == 0)
            {
                return false;
            }

            var typeToken = expectedTypeArray[0];

            // Случай COMBO: тип определяется как массив строк, например ["euler", "heun", ...]
            if (typeToken is JArray)
            {
                return value.Type == JTokenType.String;
            }

            // Случай простого типа: INT, FLOAT, STRING, BOOLEAN
            if (typeToken is JValue typeNameToken)
            {
                switch (typeNameToken.ToString())
                {
                    case "INT":
                        return value.Type == JTokenType.Integer;
                    case "FLOAT":
                        return value.Type == JTokenType.Float || value.Type == JTokenType.Integer;
                    case "STRING":
                        return value.Type == JTokenType.String;
                    case "BOOLEAN":
                        return value.Type == JTokenType.Boolean;
                    default:
                        // Неизвестный, но явно определенный тип виджета. Считаем, что подходит.
                        return true;
                }
            }
            return false;
        }

        #region Вложенные классы для моделей данных

        private class FullWorkflow { [JsonProperty("nodes")] public List<Node> Nodes { get; set; } [JsonProperty("links")] public List<List<object>> Links { get; set; } }
        private class Node { [JsonProperty("id")] public int Id { get; set; } [JsonProperty("type")] public string Type { get; set; } [JsonProperty("title")] public string Title { get; set; } [JsonProperty("inputs")] public List<Input> Inputs { get; set; } = new List<Input>(); [JsonProperty("widgets_values")] public List<object> WidgetsValues { get; set; } = new List<object>(); }
        private class Input { [JsonProperty("name")] public string Name { get; set; } [JsonProperty("link")] public long? Link { get; set; } }

        private class ApiNode { [JsonProperty("inputs")] public Dictionary<string, object> Inputs { get; set; } [JsonProperty("class_type")] public string ClassType { get; set; } [JsonProperty("_meta")] public Meta Meta { get; set; } }
        private class Meta { [JsonProperty("title")] public string Title { get; set; } }

        // --- ИСПРАВЛЕННЫЕ Модели для десериализации формата object_info ---
        private class ObjectInfoNode
        {
            [JsonProperty("input")]
            public NodeInput Input { get; set; }
            
            [JsonProperty("input_order")]
            public InputOrderInfo InputOrder { get; set; }
        }
        
        private class NodeInput 
        {
            // Используем JToken для обработки как массивов [...] так и простых строк "..."
            [JsonProperty("required")]
            public Dictionary<string, JToken> Required { get; set; }
        }

        private class InputOrderInfo
        {
            [JsonProperty("required")]
            public List<string> Required { get; set; }
        }

        #endregion
    }
}