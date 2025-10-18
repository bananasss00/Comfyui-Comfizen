// --- START OF MODIFIED FILE PythonScriptingService.cs ---

using IronPython.Hosting;
using Microsoft.Scripting;
using Microsoft.Scripting.Hosting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Windows.Media;
using Newtonsoft.Json.Linq;

namespace Comfizen
{
    // ... (ScriptContext остается без изменений) ...
    public class ScriptContext
    {
        public JObject prompt { get; }
        public Dictionary<string, object> state { get; }
        public HttpClient http { get; }
        public Action<string> log { get; }
        public AppSettings settings { get; }
        public ImageOutput output { get; }

        public ScriptContext(JObject prompt, Dictionary<string, object> state, AppSettings settings, ImageOutput output = null)
        {
            this.prompt = prompt;
            this.state = state;
            this.settings = settings;
            this.http = new HttpClient();
            this.log = (message) => Logger.LogToConsole($"[PythonScript] {message}", LogLevel.Info, Colors.Cyan);
            this.output = output;
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
                            // --- CHANGE: Redirect print() output to the UI console with LightBlue color ---
                            Logger.LogToConsole($"[Py-print] {output.TrimEnd()}", LogLevel.Info, Colors.LightBlue);
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
// --- END OF MODIFIED FILE PythonScriptingService.cs ---