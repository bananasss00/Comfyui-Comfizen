using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Media;
using Guid = TagLib.Asf.Guid;
using System.Windows.Media;
using System.Collections.Generic;

namespace Comfizen
{
    public static class Logger
    {
        private static readonly string LogDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        private static readonly string LogFilePath;
        private static readonly object _lock = new object();
        
        public static ConsoleLogService ConsoleLogServiceInstance { get; set; }
        public static event Action OnErrorLogged;

        static Logger()
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                LogFilePath = Path.Combine(LogDirectory, $"log_{DateTime.Now:yyyy-MM-dd}.txt");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Не удалось инициализировать логгер: {ex.Message}");
            }
        }

        public static void Log(string message)
        {
            if (string.IsNullOrEmpty(LogFilePath)) return;

            try
            {
                lock (_lock)
                {
                    File.AppendAllText(LogFilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [INFO] {message}{Environment.NewLine}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Не удалось записать в лог-файл: {ex.Message}");
            }
        }

        public static void Log(Exception exception, string contextMessage = "Произошло необработанное исключение")
        {
            if (exception == null || string.IsNullOrEmpty(LogFilePath)) return;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("==============================================================================");
                sb.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [ERROR] {contextMessage}");
                
                var currentException = exception;
                int level = 0;
                while (currentException != null)
                {
                    sb.AppendLine($"--- Уровень исключения {level} ---");
                    sb.AppendLine($"Тип: {currentException.GetType().FullName}");
                    sb.AppendLine($"Сообщение: {currentException.Message}");
                    sb.AppendLine($"StackTrace: {currentException.StackTrace}");
                    sb.AppendLine();

                    currentException = currentException.InnerException;
                    level++;
                }
                sb.AppendLine("==============================================================================");
                
                var fullErrorMessage = sb.ToString();

                lock (_lock)
                {
                    File.AppendAllText(LogFilePath, fullErrorMessage);
                }
                
                ConsoleLogServiceInstance?.LogError(fullErrorMessage);
                // SystemSounds.Exclamation.Play();
                OnErrorLogged?.Invoke();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Не удалось записать исключение в лог-файл: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Logs a message directly to the in-app UI console with a specific color.
        /// </summary>
        public static void LogToConsole(string message, LogLevel level, Color? color)
        {
            if (ConsoleLogServiceInstance == null)
            {
                Log($"[CONSOLE_FALLBACK] {message}"); // Fallback to file if console is not available
                return;
            }
            
            var logMessage = new LogMessage
            {
                Level = level,
                IsProgress = false,
                Segments = new List<LogMessageSegment> { new LogMessageSegment { Text = message, Color = color } }
            };
            
            ConsoleLogServiceInstance.LogMessages.Add(logMessage);
        }
    }
}