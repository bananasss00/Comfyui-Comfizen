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

        // --- Fields for managing reconnection ---
        private readonly object _connectionLock = new object();
        private Task _connectionManagerTask;

        public ObservableCollection<LogMessage> LogMessages { get; } = new ObservableCollection<LogMessage>();
        public bool IsConnected => _ws?.State == WebSocketState.Open;

        public ConsoleLogService(AppSettings settings)
        {
            _settings = settings;
            System.Windows.Data.BindingOperations.EnableCollectionSynchronization(LogMessages, new object());
        }

        // --- The ConnectAsync method now just starts the background connection manager ---
        public Task ConnectAsync()
        {
            // Use a lock to avoid race conditions when called from different threads
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
        
        // --- The DisconnectAsync method now also stops the connection manager ---
        public async Task DisconnectAsync()
        {
            // Cancel the token, which will signal the connection manager and the listening loop to stop
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                _cts.Cancel();
            }

            // Wait for the connection manager task to complete
            if (_connectionManagerTask != null)
            {
                try
                {
                    await _connectionManagerTask;
                }
                catch (OperationCanceledException) { /* Expected exception on cancellation */ }
                catch (Exception ex)
                {
                    Logger.Log(ex, "Error while waiting for the connection manager task to complete");
                }
                _connectionManagerTask = null;
            }

            // Close the WebSocket itself if it is still open
            if (_ws != null)
            {
                if (_ws.State == WebSocketState.Open || _ws.State == WebSocketState.Connecting)
                {
                    try
                    {
                        var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                        await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnecting", closeCts.Token);
                    }
                    catch (Exception) { /* Ignore errors on close */ }
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

        // --- The main loop that manages connection and reconnection ---
        private async Task ConnectionManagerLoopAsync(CancellationToken token)
        {
            int minDelayMs = 5000;    // 5 seconds
            int maxDelayMs = 60000;   // 1 minute
            int currentDelayMs = minDelayMs;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    // Create a new WebSocket instance for each attempt
                    _ws?.Dispose();
                    _ws = new ClientWebSocket();

                    var uriBuilder = new UriBuilder($"http://{_settings.ServerAddress}")
                    {
                        Scheme = "ws",
                        Path = "/ws",
                        Query = $"clientId={Guid.NewGuid()}"
                    };

                    // Try to connect with a timeout to avoid hanging forever
                    var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, connectCts.Token);
                    
                    await _ws.ConnectAsync(uriBuilder.Uri, linkedCts.Token);

                    // If connected successfully, reset the delay and start listening for messages
                    currentDelayMs = minDelayMs;
                    await ListenForMessages(token); // This method will run until the connection is lost
                }
                catch (OperationCanceledException)
                {
                    // This can be caused by either the application cancellation token or the connection timeout
                    if (token.IsCancellationRequested) break; // Exit if the application is closing
                }
                catch (Exception ex)
                {
                    // Log the error, but do not exit the loop
                    Logger.Log(ex, "Error while connecting or listening to the log WebSocket");
                }
                finally
                {
                    // Ensure resources are freed before the next attempt
                    _ws?.Dispose();
                    _ws = null;
                }

                // If we are here, it means the connection failed or was lost.
                // Wait before the next attempt.
                if (!token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(currentDelayMs, token);
                        
                        // Increase the delay for the next time (exponential backoff)
                        currentDelayMs = Math.Min(currentDelayMs * 2, maxDelayMs);
                    }
                    catch (OperationCanceledException)
                    {
                        // Exit the loop if the application is closing during the delay
                        break;
                    }
                }
            }
        }

        private async Task ListenForMessages(CancellationToken token)
        {
            var buffer = new ArraySegment<byte>(new byte[8192]);

            // This loop will run as long as the socket is open and cancellation has not been requested
            while (_ws.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                using (var ms = new MemoryStream())
                {
                    WebSocketReceiveResult result;
                    do
                    {
                        // If the token is cancelled, ReceiveAsync will throw an exception that will be caught in ConnectionManagerLoopAsync
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
                Text = message,
                Level = LogLevel.Error,
                Type = LogType.Normal
            });
        }
        
        private void ProcessMessage(string message)
        {
            try
            {
                var json = JObject.Parse(message);
                var type = json["type"]?.ToString();

                // 1. Handle standard logs (INFO, WARNING, ERROR)
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
                // 2. Handle direct output (progress bars)
                else if (type == "console_stderr_output")
                {
                    var text = json["data"]?["text"]?.ToString();
                    
                    // Remove \r and trailing whitespace to avoid "jumping"
                    if (string.IsNullOrEmpty(text)) return;
                    text = text.Replace("\r", "").TrimEnd();
                    if (string.IsNullOrEmpty(text)) return;

                    var lastMessage = LogMessages.LastOrDefault();
                    
                    // If the last message was also a progress bar, update it
                    if (lastMessage != null && lastMessage.Type == LogType.Progress)
                    {
                        lastMessage.Text = text;
                    }
                    else // Otherwise, add a new one
                    {
                        LogMessages.Add(new LogMessage
                        {
                            Text = text,
                            Level = LogLevel.Info, // We consider progress bars to be informational
                            Type = LogType.Progress
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