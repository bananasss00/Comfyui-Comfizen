// --- START OF FILE PythonScriptingService.cs ---

﻿// --- START OF MODIFIED FILE PythonScriptingService.cs ---

using IronPython.Hosting;
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

    /// <summary>
    /// Manages the execution of Python scripts using the IronPython engine.
    /// </summary>
    public class PythonScriptingService
    {
        private readonly ScriptEngine _engine;
        private static readonly Lazy<PythonScriptingService> _instance = new Lazy<PythonScriptingService>(() => new PythonScriptingService());
        public static PythonScriptingService Instance => _instance.Value;

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
                using (var memoryStream = new MemoryStream())
                using (var streamWriter = new StreamWriter(memoryStream, Encoding.UTF8))
                {
                    _engine.Runtime.IO.SetOutput(memoryStream, streamWriter);
                    _engine.Runtime.IO.SetErrorOutput(memoryStream, streamWriter);

                    var scope = _engine.CreateScope();
                    scope.SetVariable("ctx", context);

                    const string bootstrapScript = @"
import sys
import io

if hasattr(sys.stdout, 'buffer'):
    # line_buffering=True заставляет сбрасывать буфер при каждой новой строке, что полезно
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace', line_buffering=True)
    sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding='utf-8', errors='replace', line_buffering=True)
";
                    var fullScript = bootstrapScript + script;
                    
                    _engine.Execute(fullScript, scope);

                    // --- START OF FIX: Выполняем команду flush внутри Python ---
                    // Это заставит TextIOWrapper сбросить свой буфер в нижележащий memoryStream.
                    _engine.Execute("sys.stdout.flush()", scope);
                    _engine.Execute("sys.stderr.flush()", scope);
                    // --- END OF FIX ---

                    // Также сбрасываем C#-враппер на случай, если в нем что-то осталось
                    streamWriter.Flush();
                    memoryStream.Position = 0;
                    
                    using (var streamReader = new StreamReader(memoryStream))
                    {
                        string output = streamReader.ReadToEnd();
                        if (!string.IsNullOrWhiteSpace(output))
                        {
                            Logger.Log($"[Py-print] {output.Trim()}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log(ex, "Python script execution failed");
            }
        }
    }
}
// --- END OF MODIFIED FILE PythonScriptingService.cs ---