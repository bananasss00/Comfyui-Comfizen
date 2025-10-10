using System;
using System.IO;
using System.Text;

namespace Comfizen
{
    public static class Logger
    {
        private static readonly string LogDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        private static readonly string LogFilePath;
        private static readonly object _lock = new object();

        static Logger()
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                LogFilePath = Path.Combine(LogDirectory, $"log_{DateTime.Now:yyyy-MM-dd}.txt");
            }
            catch (Exception ex)
            {
                // Если настройка логгера не удалась, мы мало что можем сделать.
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

                lock (_lock)
                {
                    File.AppendAllText(LogFilePath, sb.ToString());
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Не удалось записать исключение в лог-файл: {ex.Message}");
            }
        }
    }
}