using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Newtonsoft.Json.Linq;
using PropertyChanged;
using Serilog;

namespace Comfizen
{
    [AddINotifyPropertyChangedInterface]
    public class ConsoleLogService
    {
        private AppSettings _settings;
        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        
        private readonly ConcurrentQueue<LogMessage> _logQueue = new();
        private readonly DispatcherTimer _logUpdateTimer;
        private readonly List<LogMessage> _fullLogHistory = new List<LogMessage>();

        private readonly object _connectionLock = new object();
        private Task _connectionManagerTask;

        public ObservableCollection<LogMessage> LogMessages { get; } = new ObservableCollection<LogMessage>();
        public bool IsConnected => _ws?.State == WebSocketState.Open;
        public event Action<LogLevel> OnLogReceived;
        
        // Regex to detect the execution time message.
        private static readonly Regex ExecutionTimeRegex = new Regex(@"^Prompt executed in [\d\.]+ seconds$", RegexOptions.Compiled);

        public ConsoleLogService(AppSettings settings)
        {
            _settings = settings;
            System.Windows.Data.BindingOperations.EnableCollectionSynchronization(LogMessages, new object());
            
            _logUpdateTimer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(100), // Process messages every 100ms
                DispatcherPriority.Background,  // Run at a low priority to keep UI responsive
                (sender, args) => ProcessLogQueue(),
                Application.Current.Dispatcher
            );
            _logUpdateTimer.Start();
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
            _logUpdateTimer?.Stop();
            
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
                catch (Exception ex) when (ex is WebSocketException || ex is System.Net.Http.HttpRequestException)
                {
                    Logger.Log($"Failed to connect to ComfyUI log WebSocket: {ex.Message}", LogLevel.Error);
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
        
        /// <summary>
        /// Safely enqueues a log message to be processed and added to the UI console.
        /// </summary>
        /// <param name="message">The LogMessage to add.</param>
        public void EnqueueLog(LogMessage message)
        {
            if (message != null)
            {
                _logQueue.Enqueue(message);
                
                if (message.Level >= LogLevel.Warning)
                {
                    OnLogReceived?.Invoke(message.Level);
                }
            }
        }
        
        public IEnumerable<LogMessage> GetFullHistory() => _fullLogHistory;
        
        public void ClearLogs()
        {
            _fullLogHistory.Clear();
            LogMessages.Clear();
        }
        
        private void ProcessMessage(string message)
        {
            try
            {
                var json = JObject.Parse(message);
                var type = json["type"]?.ToString();
                
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
                        var segments = AnsiColorParser.Parse(processedLine);
                        
                        string fullLineText = string.Concat(segments.Select(s => s.Text));
                        if (ExecutionTimeRegex.IsMatch(fullLineText))
                        {
                            // If it matches, override the styling for all segments in this line.
                            foreach (var segment in segments)
                            {
                                segment.Color = Colors.LightGreen;
                                segment.FontWeight = FontWeights.Bold;
                                segment.TextDecorations = TextDecorations.Underline;
                            }
                        }
                        
                        // Add each line as a separate LogMessage
                        EnqueueLog(new LogMessage
                        {
                            Source = LogSource.ComfyUI, // Set the source for filtering
                            Segments = segments,
                            Level = level,
                            IsProgress = false
                        });
                    }
                }
                else if (type == "console_stderr_output")
                {
                    var text = json["data"]?["text"]?.ToString();
                    if (string.IsNullOrEmpty(text)) return;
                    
                    text = text.Replace("\r", "").TrimEnd();
                    if (string.IsNullOrEmpty(text)) return;

                    var newSegments = AnsiColorParser.Parse(text);
                    
                    EnqueueLog(new LogMessage
                    {
                        Source = LogSource.ComfyUI, // Set the source for filtering
                        Segments = newSegments,
                        Level = LogLevel.Info,
                        IsProgress = true
                    });
                }
            }
            catch
            {
                // Ignore non-JSON messages
            }
        }

        /// <summary>
        /// Dequeues messages and adds them to the observable collection in a UI-thread-safe manner.
        /// </summary>
        private void ProcessLogQueue()
        {
            int count = 0;
            // Process up to 50 messages per tick to prevent UI lockup if logs are spammed.
            while (_logQueue.TryDequeue(out var message) && count < 50)
            {
                var lastMessage = LogMessages.LastOrDefault();

                // Handle progress bar updates by modifying the last message if it's also a progress bar.
                if (message.IsProgress && lastMessage != null && lastMessage.IsProgress)
                {
                    lastMessage.Segments = message.Segments;
                }
                else
                {
                    _fullLogHistory.Add(message);
                    LogMessages.Add(message);
                }

                if (LogMessages.Count > _settings.ConsoleDisplayLimit)
                {
                    LogMessages.RemoveAt(0);
                }

                count++;
            }
        }
    }
}