// Logger.cs
using System;
using Serilog;
using Serilog.Context; // ADDED: For LogContext
using _Log = Serilog.Log;

namespace Comfizen
{
    public static class Logger
    {
        public static event Action OnErrorLogged;

        /// <summary>
        /// Logs an informational message from the main application.
        /// </summary>
        public static void Log(string message)
        {
            _Log.Information(message);
        }
        
        /// <summary>
        /// Logs a message originating from a Python script, enriching it with context.
        /// </summary>
        public static void LogFromPython(string message)
        {
            // Push the "LogSource" property onto the context for this log event only.
            using (LogContext.PushProperty("LogSource", "Python"))
            {
                _Log.Information(message);
            }
        }

        /// <summary>
        /// Logs an exception with an accompanying message.
        /// </summary>
        public static void Log(Exception exception, string contextMessage = "An unhandled exception occurred")
        {
            _Log.Error(exception, contextMessage);
            OnErrorLogged?.Invoke();
        }
    }
}