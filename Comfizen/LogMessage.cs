using PropertyChanged;
using System;
using System.ComponentModel;

namespace Comfizen
{
    // Новый enum для определения уровня логирования
    public enum LogLevel { Info, Warning, Error, Critical, Debug }

    // Новый enum для определения, является ли сообщение обычной строкой или обновлением прогресса
    public enum LogType { Normal, Progress }

    [AddINotifyPropertyChangedInterface]
    public class LogMessage : INotifyPropertyChanged
    {
        public DateTime Timestamp { get; } = DateTime.Now;
        public string Text { get; set; }
        public LogLevel Level { get; set; }
        public LogType Type { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}