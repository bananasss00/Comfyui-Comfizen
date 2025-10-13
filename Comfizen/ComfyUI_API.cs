using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Comfizen
{
    public class FileOutput
    {
        public byte[] Data { get; set; }
        public string FileName { get; set; }
        public string FilePath { get; set; }
    }
    
    public class ComfyUI_API
    {
        private string _clientId, _serverAddress;
        private ClientWebSocket _ws = new();

        public ComfyUI_API(string serverAddress = "127.0.0.1:8188", string? clientId = null)
        {
            _clientId = clientId ?? Guid.NewGuid().ToString();
            _serverAddress = serverAddress;
        }

        public async Task Connect()
        {
            if (_ws.State == WebSocketState.Open) return;
            _ws = new ClientWebSocket(); // Recreate the instance if it's closed or aborted
            await _ws.ConnectAsync(new Uri($"ws://{_serverAddress}/ws?clientId={_clientId}"), CancellationToken.None);
        }

        public async Task<Dictionary<string, object>?> QueuePromptAsync(string prompt)
        {
            try
            {
                var promptObj = JObject.Parse(prompt);
                var requestData = new { prompt = promptObj, client_id = _clientId };
                var jsonRequest = JsonConvert.SerializeObject(requestData);

                using (var httpClient = new HttpClient())
                {
                    var requestUri = $"http://{_serverAddress}/prompt";
                    var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");
                    using (var response = await httpClient.PostAsync(requestUri, content))
                    {
                        response.EnsureSuccessStatusCode();
                        var responseString = await response.Content.ReadAsStringAsync();
                        return JsonConvert.DeserializeObject<Dictionary<string, object>>(responseString);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred in QueuePromptAsync: {ex.Message}");
                return null;
            }
        }

        public async Task<byte[]?> GetImageAsync(string filename, string subfolder, string folderType)
        {
            try
            {
                var queryString = new NameValueCollection
                {
                    ["filename"] = filename,
                    ["subfolder"] = subfolder,
                    ["type"] = folderType
                }.ToQueryString();

                var url = $"http://{_serverAddress}/view?{queryString}";

                using (var httpClient = new HttpClient())
                {
                    var responseStream = await httpClient.GetStreamAsync(url);
                    return await ReadFullyAsync(responseStream);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred in GetImageAsync: {ex.Message}");
                return null;
            }
        }
        
        public async Task<bool> InterruptAsync()
        {
            try
            {
                using (var httpClient = new HttpClient())
                {
                    var requestUri = $"http://{_serverAddress}/interrupt";
                    // Отправляем пустой POST-запрос
                    using (var response = await httpClient.PostAsync(requestUri, null))
                    {
                        response.EnsureSuccessStatusCode();
                        Debug.WriteLine("Interrupt request sent successfully.");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred in InterruptAsync: {ex.Message}");
                return false;
            }
        }

        public async Task<Dictionary<string, object>?> GetHistoryAsync(string promptId)
        {
            try
            {
                using (var httpClient = new HttpClient())
                {
                    var url = $"http://{_serverAddress}/history/{promptId}";
                    using (var response = await httpClient.GetAsync(url))
                    {
                        response.EnsureSuccessStatusCode();
                        var responseString = await response.Content.ReadAsStringAsync();
                        return JsonConvert.DeserializeObject<Dictionary<string, object>>(responseString);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred in GetHistoryAsync: {ex.Message}");
                return null;
            }
        }

        private async Task<byte[]> ReadFullyAsync(Stream input)
        {
            using (var ms = new MemoryStream())
            {
                await input.CopyToAsync(ms);
                return ms.ToArray();
            }
        }

        public async Task<Dictionary<string, List<FileOutput>>> GetImagesAsync(string prompt)
        {
            var promptId = await GetPromptIdAsync(prompt);
            if (string.IsNullOrEmpty(promptId)) return new Dictionary<string, List<FileOutput>>();

            await Connect(); // Убедимся, что сокет подключен
            await ReceiveExecutingStatusAsync(_ws, promptId);
            var outputImages = await ExtractImagesFromHistoryAsync(promptId);
            return outputImages;
        }

        private async Task<string> GetPromptIdAsync(string prompt)
        {
            var response = await QueuePromptAsync(prompt);
            return response?["prompt_id"]?.ToString();
        }

        // ====================================================================
        // ИЗМЕНЕННЫЙ МЕТОД
        // ====================================================================
        private async Task ReceiveExecutingStatusAsync(ClientWebSocket ws, string promptId)
        {
            var buffer = new byte[1024 * 4]; // Буфер для чтения

            while (ws.State == WebSocketState.Open)
            {
                using (var ms = new MemoryStream())
                {
                    WebSocketReceiveResult result;
                    do
                    {
                        // Читаем фрагмент в буфер
                        result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                            return; // Выходим, если сервер закрыл соединение
                        }

                        // Записываем полученный фрагмент в MemoryStream
                        ms.Write(buffer, 0, result.Count);

                    } while (!result.EndOfMessage); // Повторяем, пока не получим флаг конца сообщения

                    // Теперь, когда все фрагменты собраны, преобразуем их в строку
                    var message = Encoding.UTF8.GetString(ms.ToArray());

                    try
                    {
                        var msgData = JsonConvert.DeserializeObject<Dictionary<string, object>>(message);
                        if (msgData == null) continue;

                        var type = msgData["type"]?.ToString();
                        if (type == "executing")
                        {
                            // Проверяем, не является ли это сообщением о завершении всего промпта
                            if (IsExecutionComplete(msgData, promptId))
                            {
                                return; // Выполнение завершено, выходим из метода
                            }
                        }
                        // Можно добавить обработку других типов сообщений, например, прогресса
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine($"Error deserializing WebSocket message: {e.Message}");
                    }
                }
            }
        }

        private bool IsExecutionComplete(Dictionary<string, object> msgData, string promptId)
        {
            if (msgData.TryGetValue("data", out var dataObj) && dataObj is JObject dataJObject)
            {
                var data = dataJObject.ToObject<Dictionary<string, object>>();
                // Сообщение о завершении выглядит так: 'node' is null, 'prompt_id' совпадает
                if (data.TryGetValue("node", out var node) && node == null)
                {
                    if (data.TryGetValue("prompt_id", out var promptIdObj) && promptIdObj.ToString() == promptId)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private async Task<Dictionary<string, List<FileOutput>>> ExtractImagesFromHistoryAsync(string promptId)
        {
            var response = await GetHistoryAsync(promptId);
            if (response == null || !response.ContainsKey(promptId)) return new Dictionary<string, List<FileOutput>>();

            var history = ((JObject)response[promptId]).ToObject<Dictionary<string, object>>();
            var outputs = ((JObject)history["outputs"]).ToObject<Dictionary<string, object>>();

            var outputFiles = new Dictionary<string, List<FileOutput>>();

            foreach (var nodeId in outputs)
            {
                if (nodeId.Value is JObject nodeOutput)
                {
                    var filesOutput = new List<FileOutput>();
                    
                    // ====================================================================
                    // ИЗМЕНЕНИЕ ЗДЕСЬ: Функция для обработки любого списка файлов
                    // ====================================================================
                    async Task ProcessFiles(JToken filesToken)
                    {
                        if (filesToken is JArray files)
                        {
                            foreach (var file in files)
                            {
                                var fileDict = file.ToObject<Dictionary<string, string>>();
                                if (fileDict != null)
                                {
                                    var fileData = await GetImageAsync(fileDict["filename"], fileDict["subfolder"], fileDict["type"]);
                                    if (fileData != null)
                                    {
                                        var filePath = Path.Combine(fileDict["type"], fileDict["subfolder"], fileDict["filename"]);
                                        filesOutput.Add(new FileOutput { Data = fileData, FileName = fileDict["filename"], FilePath = filePath });
                                    }
                                }
                            }
                        }
                    }

                    // Проверяем ключ "images"
                    if (nodeOutput.TryGetValue("images", out var imagesToken))
                    {
                        await ProcessFiles(imagesToken);
                    }

                    // Проверяем ключ "gifs" (обычно для видео и анимации)
                    if (nodeOutput.TryGetValue("gifs", out var gifsToken))
                    {
                        await ProcessFiles(gifsToken);
                    }

                    if (filesOutput.Count > 0)
                    {
                        outputFiles[nodeId.Key] = filesOutput;
                    }
                }
            }

            return outputFiles;
        }
    }
}