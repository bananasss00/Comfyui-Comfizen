using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using PropertyChanged;

namespace Comfizen
{
    [AddINotifyPropertyChangedInterface]
    public class ConsoleLogService
    {
        private AppSettings _settings;
        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;

        public ObservableCollection<LogMessage> LogMessages { get; } = new ObservableCollection<LogMessage>();
        public bool IsConnected => _ws?.State == WebSocketState.Open;

        public ConsoleLogService(AppSettings settings)
        {
            _settings = settings;
            System.Windows.Data.BindingOperations.EnableCollectionSynchronization(LogMessages, new object());
        }

        public async Task ConnectAsync()
        {
            if (IsConnected) return;

            _cts = new CancellationTokenSource();
            _ws = new ClientWebSocket();

            try
            {
                var uriBuilder = new UriBuilder($"http://{_settings.ServerAddress}");
                uriBuilder.Scheme = uriBuilder.Scheme.Replace("http", "ws");
                uriBuilder.Path = "/ws";
                uriBuilder.Query = $"clientId={Guid.NewGuid()}";

                await _ws.ConnectAsync(uriBuilder.Uri, _cts.Token);
                
                _ = Task.Run(() => ListenForMessages(_cts.Token), _cts.Token);
            }
            catch (Exception ex)
            {
                Logger.Log(ex, "Не удалось подключиться к WebSocket для логов");
                await DisconnectAsync();
            }
        }

        public async Task DisconnectAsync()
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                _cts.Cancel();
            }

            if (_ws != null)
            {
                if (_ws.State == WebSocketState.Open || _ws.State == WebSocketState.Connecting)
                {
                    try
                    {
                        // Даем короткий таймаут на закрытие
                        var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnecting", closeCts.Token);
                    }
                    catch (Exception ex)
                    {
                         Logger.Log(ex, "Ошибка при закрытии WebSocket");
                    }
                }
                _ws.Dispose();
                _ws = null;
            }
            _cts?.Dispose();
            _cts = null;
        }

        public async Task ReconnectAsync(AppSettings newSettings)
        {
            await DisconnectAsync();
            _settings = newSettings;
            await ConnectAsync();
        }

        private async Task ListenForMessages(CancellationToken token)
        {
            var buffer = new ArraySegment<byte>(new byte[8192]);

            try
            {
                while (_ws.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    using (var ms = new MemoryStream())
                    {
                        WebSocketReceiveResult result;
                        do
                        {
                            result = await _ws.ReceiveAsync(buffer, token);
                            ms.Write(buffer.Array, buffer.Offset, result.Count);
                        } while (!result.EndOfMessage);

                        if (result.MessageType == WebSocketMessageType.Close) break;

                        var message = Encoding.UTF8.GetString(ms.ToArray());
                        ProcessMessage(message);
                    }
                }
            }
            catch (OperationCanceledException) { /* Нормальное завершение */ }
            catch (Exception ex)
            {
                Logger.Log(ex, "Ошибка при прослушивании WebSocket");
            }
        }

        private void ProcessMessage(string message)
        {
            try
            {
                var json = JObject.Parse(message);
                var type = json["type"]?.ToString();

                // 1. Обработка стандартных логов (INFO, WARNING, ERROR)
                if (type == "console_log_message")
                {
                    var data = json["data"];
                    var levelStr = data?["level"]?.ToString();
                    var text = data?["message"]?.ToString();

                    if (string.IsNullOrEmpty(text)) return;

                    Enum.TryParse<LogLevel>(levelStr, true, out var level);
                    
                    LogMessages.Add(new LogMessage
                    {
                        Text = text,
                        Level = level,
                        Type = LogType.Normal
                    });
                }
                // 2. Обработка прямого вывода (прогресс-бары)
                else if (type == "console_stderr_output")
                {
                    var text = json["data"]?["text"]?.ToString();
                    
                    // Убираем \r и лишние пробелы в конце, чтобы избежать "прыганья"
                    if (string.IsNullOrEmpty(text)) return;
                    text = text.Replace("\r", "").TrimEnd();
                    if (string.IsNullOrEmpty(text)) return;

                    var lastMessage = LogMessages.LastOrDefault();
                    
                    // Если последнее сообщение было тоже прогресс-баром, обновляем его
                    if (lastMessage != null && lastMessage.Type == LogType.Progress)
                    {
                        lastMessage.Text = text;
                    }
                    else // Иначе, добавляем новое
                    {
                        LogMessages.Add(new LogMessage
                        {
                            Text = text,
                            Level = LogLevel.Info, // Прогресс-бары считаем информационными
                            Type = LogType.Progress
                        });
                    }
                }
            }
            catch
            {
                // Игнорируем не-JSON сообщения
            }
        }
    }
}