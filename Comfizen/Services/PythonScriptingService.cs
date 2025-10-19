using IronPython.Hosting;
using Microsoft.Scripting.Hosting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks; // Добавлено для асинхронных операций
using System.Windows.Media;
using Newtonsoft.Json; // Добавлено для сериализации
using Newtonsoft.Json.Linq;
using IronPython.Runtime; // Добавлено для работы со словарями Python

namespace Comfizen
{
    /// <summary>
    /// Provides context and helper methods to Python scripts.
    /// An instance of this class is available as 'ctx' inside the script.
    /// </summary>
    public class ScriptContext
    {
        private readonly HttpClient _http; 
        
        public JObject prompt { get; }
        public Dictionary<string, object> state { get; }
        public Action<string> log { get; }
        public AppSettings settings { get; }
        public ImageOutput output { get; }

        public ScriptContext(JObject prompt, Dictionary<string, object> state, AppSettings settings, ImageOutput output = null)
        {
            this.prompt = prompt;
            this.state = state;
            this.settings = settings;
            this._http = new HttpClient();
            this.log = (message) => Logger.LogToConsole($"[py] {message}", LogLevel.Info, Colors.Cyan);
            this.output = output;
        }

        /// <summary>
        /// Executes an HTTP GET request and returns the response body as a string.
        /// </summary>
        /// <param name="url">The URL to send the request to.</param>
        /// <returns>The response content as a string, or null if an error occurs.</returns>
        public async Task<string> get(string url)
        {
            try
            {
                // --- START OF FIX: Use ConfigureAwait(false) to prevent deadlocks ---
                // This tells the task that it does not need to resume on the original UI thread context.
                // It can continue on any available thread pool thread, which avoids a deadlock when
                // the calling script is blocking the UI thread with .Result or .Wait().
                var response = await _http.GetAsync(url).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                // --- END OF FIX ---
            }
            catch (Exception ex)
            {
                log($"GET request to '{url}' failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Executes an HTTP POST request with the given data and returns the response body as a string.
        /// </summary>
        /// <param name="url">The URL to send the request to.</param>
        /// <param name="data">The data to post. Can be a Python dictionary (will be sent as JSON) or a string.</param>
        /// <returns>The response content as a string, or null if an error occurs.</returns>
        public async Task<string> post(string url, object data)
        {
            try
            {
                string payload;
                
                if (data is PythonDictionary dict)
                {
                    payload = JsonConvert.SerializeObject(dict);
                }
                else
                {
                    payload = data?.ToString() ?? string.Empty;
                }

                var content = new StringContent(payload, Encoding.UTF8, "application/json");

                // --- START OF FIX: Use ConfigureAwait(false) to prevent deadlocks ---
                // The same principle as in the get() method applies here.
                var response = await _http.PostAsync(url, content).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                
                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                // --- END OF FIX ---
            }
            catch (Exception ex)
            {
                log($"POST request to '{url}' failed: {ex.Message}");
                return null;
            }
        }
    }
    
    public class PythonScriptingService
    {
        private readonly ScriptEngine _engine;
        private static readonly Lazy<PythonScriptingService> _instance = new Lazy<PythonScriptingService>(() => new PythonScriptingService());
        public static PythonScriptingService Instance => _instance.Value;

        static PythonScriptingService()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        private PythonScriptingService()
        {
            _engine = Python.CreateEngine();
            var stdLibPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Lib");
            if (Directory.Exists(stdLibPath))
            {
                var searchPaths = _engine.GetSearchPaths().ToList();
                if (!searchPaths.Contains(stdLibPath))
                {
                    searchPaths.Add(stdLibPath);
                }
                _engine.SetSearchPaths(searchPaths);
            }
        }

        public void Execute(string script, ScriptContext context)
        {
            if (string.IsNullOrWhiteSpace(script)) return;

            try
            {
                var utf8Encoding = new UTF8Encoding(false);
                using (var memoryStream = new MemoryStream())
                {
                    _engine.Runtime.IO.SetOutput(memoryStream, utf8Encoding);
                    _engine.Runtime.IO.SetErrorOutput(memoryStream, utf8Encoding);

                    var scope = _engine.CreateScope();
                    scope.SetVariable("ctx", context);
                    
                    _engine.Execute(script, scope);

                    _engine.Runtime.IO.OutputWriter.Flush();
                    
                    memoryStream.Position = 0;
                    using (var streamReader = new StreamReader(memoryStream, utf8Encoding))
                    {
                        string output = streamReader.ReadToEnd();
                        if (!string.IsNullOrWhiteSpace(output))
                        {
                            Logger.LogToConsole($"[py] {output.TrimEnd()}", LogLevel.Info, Colors.LightBlue);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                var eo = _engine.GetService<ExceptionOperations>();
                string errorDetails = eo.FormatException(ex);
                Logger.Log(new Exception(errorDetails, ex), "Python script execution failed");
            }
        }
    }
}