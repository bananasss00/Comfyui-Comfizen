using PropertyChanged;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace Comfizen
{
    public enum LogLevel { Info, Warning, Error, Critical, Debug }

    /// <summary>
    /// Represents a single text segment in a log message with its own color.
    /// </summary>
    public class LogMessageSegment
    {
        public string Text { get; set; }
        public Color? Color { get; set; } // Nullable to allow using the default color
        public FontWeight FontWeight { get; set; } = FontWeights.Normal;
        public TextDecorationCollection TextDecorations { get; set; } = null;
    }

    [AddINotifyPropertyChangedInterface]
    public class LogMessage : INotifyPropertyChanged
    {
        public DateTime Timestamp { get; } = DateTime.Now;
        public List<LogMessageSegment> Segments { get; set; }
        public LogLevel Level { get; set; }
        
        /// <summary>
        /// Indicates if this message is a progress bar update.
        /// </summary>
        public bool IsProgress { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}