using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Media;
using Guid = TagLib.Asf.Guid;
using System.Windows.Media;
using System.Collections.Generic;
using Serilog;
using _Log = Serilog.Log;

namespace Comfizen
{
    public static class Logger
    {
        // The ConsoleLogServiceInstance is now only needed for the OnErrorLogged event.
        public static ConsoleLogService ConsoleLogServiceInstance { get; set; }
        public static event Action OnErrorLogged;

        // --- START OF CHANGE: Remove all old file writing logic ---
        // The static constructor, _lock, LogDirectory, and LogFilePath are no longer needed.
        // Serilog handles all of this automatically.

        public static void Log(string message)
        {
            // Use Serilog's global logger.
            _Log.Information(message);
        }

        public static void Log(Exception exception, string contextMessage = "An unhandled exception occurred")
        {
            // Serilog has first-class support for exceptions.
            // The contextMessage will be the main log entry, and the exception details will be appended automatically.
            _Log.Error(exception, contextMessage);
            
            // This part for UI notification remains the same.
            OnErrorLogged?.Invoke();
        }
        
        // <summary>
        /// Logs a message directly to the in-app UI console with a specific color.
        /// </summary>
        public static void LogToConsole(string message, LogLevel level, Color? color)
        {
            if (ConsoleLogServiceInstance == null)
            {
                Log($"[CONSOLE_FALLBACK] {message}"); // Fallback to file if console is not available
                return;
            }
            ConsoleLogServiceInstance.LogApplicationMessage(message, level, color);
        }
    }
}