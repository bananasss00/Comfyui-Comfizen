// ConsoleLogServiceSink.cs
using System;
using System.IO;
using System.Windows.Media;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;

namespace Comfizen;

public class ConsoleLogServiceSink : ILogEventSink
{
    private readonly ConsoleLogService _consoleLogService;
    private readonly ITextFormatter _formatter = new MessageTemplateTextFormatter("{Message:lj}");

    public ConsoleLogServiceSink(ConsoleLogService consoleLogService)
    {
        _consoleLogService = consoleLogService ?? throw new ArgumentNullException(nameof(consoleLogService));
    }

    public void Emit(LogEvent logEvent)
    {
        if (_consoleLogService == null) return;

        using var writer = new StringWriter();
        _formatter.Format(logEvent, writer);

        var messageText = writer.ToString().TrimEnd('\r', '\n');
        
        var level = logEvent.Level switch
        {
            LogEventLevel.Verbose => LogLevel.Debug,
            LogEventLevel.Debug => LogLevel.Debug,
            LogEventLevel.Information => LogLevel.Info,
            LogEventLevel.Warning => LogLevel.Warning,
            LogEventLevel.Error => LogLevel.Error,
            LogEventLevel.Fatal => LogLevel.Critical,
            _ => LogLevel.Info
        };

        var segments = AnsiColorParser.Parse(messageText);
        var logMessage = new LogMessage { Level = level, Segments = segments };
        
        if (logEvent.Properties.TryGetValue("LogSource", out var sourceValue) &&
            sourceValue is ScalarValue { Value: "Python" })
        {
            logMessage.Source = LogSource.Python;
            logMessage.Segments.Insert(0, new LogMessageSegment { Text = "[Py] ", Color = Colors.SteelBlue });
        }
        else
        {
            logMessage.Source = LogSource.Application;
            logMessage.Segments.Insert(0, new LogMessageSegment { Text = "[App] ", Color = Colors.DarkGray });
        }
        
        if (logEvent.Exception != null)
        {
            logMessage.Segments.Add(new LogMessageSegment { Text = Environment.NewLine + logEvent.Exception });
        }

        _consoleLogService.EnqueueLog(logMessage);
    }
}