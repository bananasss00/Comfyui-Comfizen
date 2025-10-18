using System;
using System.Collections.Generic;
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

        private readonly object _connectionLock = new object();
        private Task _connectionManagerTask;

        public ObservableCollection<LogMessage> LogMessages { get; } = new ObservableCollection<LogMessage>();
        public bool IsConnected => _ws?.State == WebSocketState.Open;

        public ConsoleLogService(AppSettings settings)
        {
            _settings = settings;
            System.Windows.Data.BindingOperations.EnableCollectionSynchronization(LogMessages, new object());
        }

        public Task ConnectAsync()
        {
            lock (_connectionLock)
            {
                if (_connectionManagerTask == null || _connectionManagerTask.IsCompleted)
                {
                    _cts = new CancellationTokenSource();
                    _connectionManagerTask = Task.Run(() => ConnectionManagerLoopAsync(_cts.Token));
                }
            }
            return Task.CompletedTask;
        }
        
        public async Task DisconnectAsync()
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                _cts.Cancel();
            }

            if (_connectionManagerTask != null)
            {
                try
                {
                    await _connectionManagerTask;
                }
                catch (OperationCanceledException) { /* Expected */ }
                catch (Exception ex)
                {
                    Logger.Log(ex, "Error while waiting for the connection manager task to complete");
                }
                _connectionManagerTask = null;
            }

            if (_ws != null)
            {
                if (_ws.State == WebSocketState.Open || _ws.State == WebSocketState.Connecting)
                {
                    try
                    {
                        var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                        await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnecting", closeCts.Token);
                    }
                    catch (Exception) { /* Ignore */ }
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

        private async Task ConnectionManagerLoopAsync(CancellationToken token)
        {
            int minDelayMs = 5000;
            int maxDelayMs = 60000;
            int currentDelayMs = minDelayMs;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    _ws?.Dispose();
                    _ws = new ClientWebSocket();

                    var uriBuilder = new UriBuilder($"http://{_settings.ServerAddress}")
                    {
                        Scheme = "ws",
                        Path = "/ws",
                        Query = $"clientId={Guid.NewGuid()}"
                    };

                    var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, connectCts.Token);
                    
                    await _ws.ConnectAsync(uriBuilder.Uri, linkedCts.Token);

                    currentDelayMs = minDelayMs;
                    await ListenForMessages(token);
                }
                catch (OperationCanceledException)
                {
                    if (token.IsCancellationRequested) break;
                }
                catch (Exception ex)
                {
                    Logger.Log(ex, "Error while connecting or listening to the log WebSocket");
                }
                finally
                {
                    _ws?.Dispose();
                    _ws = null;
                }

                if (!token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(currentDelayMs, token);
                        currentDelayMs = Math.Min(currentDelayMs * 2, maxDelayMs);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }

        private async Task ListenForMessages(CancellationToken token)
        {
            var buffer = new ArraySegment<byte>(new byte[8192]);

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
        
        public void LogError(string message)
        {
            LogMessages.Add(new LogMessage
            {
                Segments = new List<LogMessageSegment> { new LogMessageSegment { Text = message } },
                Level = LogLevel.Error,
                IsProgress = false
            });
        }
        
        private void ProcessMessage(string message)
        {
            try
            {
                var json = JObject.Parse(message);
                var type = json["type"]?.ToString();
                
                // --- START OF FIX: Handle multi-line console messages ---
                if (type == "console_log_message" || type == "console_stdout_output")
                {
                    var data = json["data"];
                    var text = (type == "console_log_message" ? data?["message"]?.ToString() : data?["text"]?.ToString());
                    if (string.IsNullOrEmpty(text)) return;
                    
                    var levelStr = data?["level"]?.ToString() ?? "Info";
                    Enum.TryParse<LogLevel>(levelStr, true, out var level);
                    
                    // Split the incoming text by newlines
                    var lines = text.Split('\n');

                    foreach (var line in lines)
                    {
                        // Trim the carriage return that often comes with newlines (\r\n)
                        var processedLine = line.TrimEnd('\r');
                        
                        // Add each line as a separate LogMessage
                        LogMessages.Add(new LogMessage
                        {
                            Segments = AnsiColorParser.Parse(processedLine),
                            Level = level,
                            IsProgress = false
                        });
                    }
                }
                // --- END OF FIX ---
                
                // 2. Handle stderr (progress bars) - this part remains the same
                else if (type == "console_stderr_output")
                {
                    var text = json["data"]?["text"]?.ToString();
                    if (string.IsNullOrEmpty(text)) return;
                    
                    text = text.Replace("\r", "").TrimEnd();
                    if (string.IsNullOrEmpty(text)) return;

                    var newSegments = AnsiColorParser.Parse(text);
                    var lastMessage = LogMessages.LastOrDefault();
                    
                    if (lastMessage != null && lastMessage.IsProgress)
                    {
                        lastMessage.Segments = newSegments;
                    }
                    else
                    {
                        LogMessages.Add(new LogMessage
                        {
                            Segments = newSegments,
                            Level = LogLevel.Info,
                            IsProgress = true
                        });
                    }
                }
            }
            catch
            {
                // Ignore non-JSON messages
            }
        }
    }
}