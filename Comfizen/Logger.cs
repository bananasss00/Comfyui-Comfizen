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
        /// Logs a message with a specified log level.
        /// </summary>
        /// <param name="message">The message to log.</param>
        /// <param name="level">The severity level of the log message. Defaults to Info.</param>
        public static void Log(string message, LogLevel level = LogLevel.Info)
        {
            switch (level)
            {
                case LogLevel.Debug:
                    _Log.Debug(message);
                    break;
                case LogLevel.Warning:
                    _Log.Warning(message);
                    break;
                case LogLevel.Error:
                    _Log.Error(message);
                    OnErrorLogged?.Invoke();
                    break;
                case LogLevel.Critical:
                    _Log.Fatal(message); // Serilog uses "Fatal" for critical level
                    OnErrorLogged?.Invoke();
                    break;
                case LogLevel.Info:
                default:
                    _Log.Information(message);
                    break;
            }
        }
        
        /// <summary>
        /// Logs a message only to the in-app UI console, not to the log file.
        /// </summary>
        public static void LogToConsole(string message)
        {
            // Push a special property that the file sink can use to filter this message out.
            using (LogContext.PushProperty("ConsoleOnly", true))
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