using IronPython.Hosting;
using Microsoft.Scripting.Hosting;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks; // Добавлено для асинхронных операций
using System.Windows.Media;
using Newtonsoft.Json; // Добавлено для сериализации
using Newtonsoft.Json.Linq;
using IronPython.Runtime;
using Serilog.Context; // Добавлено для работы со словарями Python

namespace Comfizen
{
    /// <summary>
    /// Provides context and helper methods to Python scripts.
    /// An instance of this class is available as 'ctx' inside the script.
    /// </summary>
    public class ScriptContext
    {
        private readonly HttpClient _http; 
        
        public PythonDictionary prompt { get; }
        public Dictionary<string, object> state { get; }
        public Action<string> log { get; }
        public AppSettings settings { get; }
        public ImageOutput output { get; }
        private readonly Action<JObject> _queue_prompt_action;
        private readonly Action<string> _apply_global_preset_action;
        private readonly Action<string, string> _apply_group_preset_action;

        public ScriptContext(JObject prompt, Dictionary<string, object> state, AppSettings settings, 
            Action<JObject> queue_prompt_action, 
            Action<string> applyGlobalPresetAction,
            Action<string, string> applyGroupPresetAction,
            ImageOutput output = null)
        {
            this.prompt = ConvertJObjectToPythonDict(prompt);
            this.state = state;
            this.settings = settings;
            this._http = new HttpClient();
            this.log = (message) => 
            {
                using (LogContext.PushProperty("LogSource", "Python"))
                {
                    Logger.LogToConsole(message);
                }
            };
            this.output = output;
            this._queue_prompt_action = queue_prompt_action;
            this._apply_global_preset_action = applyGlobalPresetAction;
            this._apply_group_preset_action = applyGroupPresetAction;
        }
        
        /// <summary>
        /// Applies a global preset by its name.
        /// </summary>
        /// <param name="preset_name">The name of the global preset to apply.</param>
        public void apply_global_preset(string preset_name)
        {
            if (string.IsNullOrEmpty(preset_name))
            {
                log("Error: apply_global_preset was called with an empty name.");
                return;
            }
            _apply_global_preset_action?.Invoke(preset_name);
        }

        /// <summary>
        /// Applies a local group preset (Snippet or Layout) by its name.
        /// </summary>
        /// <param name="group_name">The name of the group that owns the preset.</param>
        /// <param name="preset_name">The name of the preset to apply.</param>
        public void apply_group_preset(string group_name, string preset_name)
        {
            if (string.IsNullOrEmpty(group_name) || string.IsNullOrEmpty(preset_name))
            {
                log("Error: apply_group_preset was called with an empty group or preset name.");
                return;
            }
            _apply_group_preset_action?.Invoke(group_name, preset_name);
        }
        
        /// <summary>
        /// Queues a new prompt for generation.
        /// </summary>
        /// <param name="prompt_to_queue">A PythonDictionary representing the prompt. You can pass a modified ctx.prompt.</param>
        public void queue(PythonDictionary prompt_to_queue)
        {
            try
            {
                // --- ГЛАВНОЕ ИЗМЕНЕНИЕ: КОНВЕРТИРУЕМ СЛОВАРЬ PYTHON ОБРАТНО В JObject ---
                var promptJObject = ConvertPythonDictToJObject(prompt_to_queue);
                _queue_prompt_action?.Invoke(promptJObject);
            }
            catch (Exception ex)
            {
                log($"Failed to queue prompt from script: {ex.Message}");
            }
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
                log($"GET request to '{url}' failed: {ex}");
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
                log($"POST request to '{url}' failed: {ex}");
                return null;
            }
        }
        
        private PythonDictionary ConvertJObjectToPythonDict(JObject jObject)
        {
            var dict = new PythonDictionary();
            if (jObject == null) return dict;

            foreach (var property in jObject.Properties())
            {
                dict[property.Name] = ConvertJTokenToPythonObject(property.Value);
            }
            return dict;
        }

        private object ConvertJTokenToPythonObject(JToken token)
        {
            if (token == null) return null;
            switch (token.Type)
            {
                case JTokenType.Object:
                    return ConvertJObjectToPythonDict((JObject)token);
                case JTokenType.Array:
                    var list = new List<object>();
                    foreach (var item in token.Children())
                    {
                        list.Add(ConvertJTokenToPythonObject(item));
                    }
                    return list;
                case JTokenType.Integer:
                    return token.ToObject<long>();
                case JTokenType.Float:
                    return token.ToObject<double>();
                case JTokenType.String:
                    return token.ToObject<string>();
                case JTokenType.Boolean:
                    return token.ToObject<bool>();
                default:
                    return token.ToString();
            }
        }

        private JObject ConvertPythonDictToJObject(IDictionary<object, object> dict)
        {
            var jObject = new JObject();
            if (dict == null) return jObject;

            foreach (var kvp in dict)
            {
                if (kvp.Key is string key)
                {
                    jObject[key] = ConvertPythonObjectToJToken(kvp.Value);
                }
            }
            return jObject;
        }

        private JToken ConvertPythonObjectToJToken(object obj)
        {
            if (obj == null) return JValue.CreateNull();

            if (obj is IDictionary<object, object> dict)
            {
                return ConvertPythonDictToJObject(dict);
            }
            if (obj is IList list)
            {
                var jArray = new JArray();
                foreach (var item in list)
                {
                    jArray.Add(ConvertPythonObjectToJToken(item));
                }
                return jArray;
            }
            
            // JToken.FromObject отлично справляется с базовыми типами
            return JToken.FromObject(obj);
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
                            using (LogContext.PushProperty("LogSource", "Python"))
                            {
                                Logger.LogToConsole(output.TrimEnd());
                            }
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