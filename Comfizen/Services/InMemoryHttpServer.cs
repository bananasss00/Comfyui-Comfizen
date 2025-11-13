using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Comfizen;

/// <summary>
/// A singleton in-memory HTTP server to stream byte arrays to the WPF MediaElement.
/// </summary>
public sealed class InMemoryHttpServer
{
    private static readonly Lazy<InMemoryHttpServer> lazy = new Lazy<InMemoryHttpServer>(() => new InMemoryHttpServer());
    public static InMemoryHttpServer Instance => lazy.Value;

    private readonly HttpListener _listener;
    private readonly string _baseUrl;
    private readonly ConcurrentDictionary<string, byte[]> _mediaStore = new ConcurrentDictionary<string, byte[]>();
    private CancellationTokenSource _cts;

    private InMemoryHttpServer()
    {
        // Use a random high port to avoid conflicts
        var random = new Random();
        int port = random.Next(49152, 65535);
        _baseUrl = $"http://127.0.0.1:{port}/";

        _listener = new HttpListener();
        _listener.Prefixes.Add(_baseUrl);
    }

    public void Start()
    {
        if (_listener.IsListening) return;

        try
        {
            _cts = new CancellationTokenSource();
            _listener.Start();
            Task.Run(() => ListenLoop(_cts.Token));
        }
        catch (Exception ex)
        {
            Logger.Log(ex, "Failed to start InMemoryHttpServer");
            var message = string.Format(LocalizationService.Instance["Server_StartError"], ex.Message);
            var title = LocalizationService.Instance["Server_PlaybackErrorTitle"];
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void Stop()
    {
        if (!_listener.IsListening) return;

        _cts?.Cancel();
        try
        {
            _listener.Stop();
            _listener.Close();
        }
        catch (Exception) { /* Ignore exceptions on shutdown */ }

        _cts?.Dispose();
        _mediaStore.Clear();
    }

    public Uri RegisterMedia(byte[] mediaBytes, string fileName)
    {
        var mediaId = Guid.NewGuid().ToString();
        _mediaStore.TryAdd(mediaId, mediaBytes);

        // Add a fake filename to the URL to help MediaElement with content type
        return new Uri($"{_baseUrl}{mediaId}/{Uri.EscapeDataString(fileName)}");
    }

    private async Task ListenLoop(CancellationToken token)
    {
        try
        {
            while (_listener.IsListening && !token.IsCancellationRequested)
            {
                var context = await _listener.GetContextAsync();
                // Process each request in its own task to avoid blocking the listener loop
                _ = ProcessRequest(context, token);
            }
        }
        catch (HttpListenerException) { /* Listener was stopped, which is expected. */ }
        catch (ObjectDisposedException) { /* Listener was closed, which is also expected. */ }
        catch (Exception ex)
        {
            Logger.Log(ex, "Exception in InMemoryHttpServer ListenLoop");
        }
    }

    private async Task ProcessRequest(HttpListenerContext context, CancellationToken token)
    {
        var requestUrl = context.Request.Url.AbsolutePath;
        var segments = requestUrl.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        
        if (segments.Length > 0)
        {
            var mediaId = segments[0];
            if (_mediaStore.TryGetValue(mediaId, out var mediaBytes))
            {
                try
                {
                    context.Response.ContentType = segments.Length > 1 && segments[1].EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ? "image/gif" : "video/mp4";
                    context.Response.AddHeader("Accept-Ranges", "bytes");

                    var rangeHeader = context.Request.Headers["Range"];
                    long start = 0;
                    long end = mediaBytes.Length - 1;

                    if (!string.IsNullOrEmpty(rangeHeader))
                    {
                        var range = rangeHeader.Replace("bytes=", "").Split('-');
                        start = long.Parse(range[0]);
                        if (range.Length > 1 && !string.IsNullOrEmpty(range[1]))
                        {
                            end = long.Parse(range[1]);
                        }
                        
                        end = Math.Min(end, mediaBytes.Length - 1);
                        if (start >= mediaBytes.Length)
                        {
                            context.Response.StatusCode = (int)HttpStatusCode.RequestedRangeNotSatisfiable;
                            context.Response.OutputStream.Close();
                            return;
                        }
                        
                        context.Response.StatusCode = (int)HttpStatusCode.PartialContent;
                        context.Response.AddHeader("Content-Range", $"bytes {start}-{end}/{mediaBytes.Length}");
                    }
                    else
                    {
                        context.Response.StatusCode = (int)HttpStatusCode.OK;
                    }
                    
                    long length = end - start + 1;
                    context.Response.ContentLength64 = length;
                    
                    await context.Response.OutputStream.WriteAsync(mediaBytes, (int)start, (int)length, token);
                }
                catch (HttpListenerException) { /* Client disconnected, common scenario. */ }
                catch (OperationCanceledException) { /* Server is shutting down. */ }
                finally
                {
                    context.Response.OutputStream.Close();
                }
                return;
            }
        }

        // If we get here, the media was not found
        context.Response.StatusCode = (int)HttpStatusCode.NotFound;
        context.Response.OutputStream.Close();
    }
}