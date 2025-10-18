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
            this.log = (message) => Logger.Log($"[PythonScript] {message}");
            this.output = output;
        }
    }
    
    public class PythonScriptingService
    {
        private readonly ScriptEngine _engine;
        private static readonly Lazy<PythonScriptingService> _instance = new Lazy<PythonScriptingService>(() => new PythonScriptingService());
        public static PythonScriptingService Instance => _instance.Value;

        // --- START OF FIX ---
        /// <summary>
        /// Статический конструктор. Выполняется один раз при первом обращении к классу.
        /// Регистрирует провайдер кодировок, необходимый для работы IronPython в среде .NET Core / .NET 5+.
        /// </summary>
        static PythonScriptingService()
        {
            // Это исправляет фундаментальную причину ошибки 'unknown encoding: codepage___0'.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }
        // --- END OF FIX ---

        private PythonScriptingService()
        {
            _engine = Python.CreateEngine();

            // Убеждаемся, что движок знает путь к стандартной библиотеке (папке Lib)
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
                    // Теперь, когда движок инициализирован правильно, этот метод будет работать корректно
                    _engine.Runtime.IO.SetOutput(memoryStream, utf8Encoding);
                    _engine.Runtime.IO.SetErrorOutput(memoryStream, utf8Encoding);

                    var scope = _engine.CreateScope();
                    scope.SetVariable("ctx", context);
                    
                    _engine.Execute(script, scope);

                    // Сбрасываем буферы Python, чтобы получить вывод
                    _engine.Runtime.IO.OutputWriter.Flush();
                    
                    memoryStream.Position = 0;
                    using (var streamReader = new StreamReader(memoryStream, utf8Encoding))
                    {
                        string output = streamReader.ReadToEnd();
                        if (!string.IsNullOrWhiteSpace(output))
                        {
                            Logger.Log($"[Py-print] {output.TrimEnd()}");
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