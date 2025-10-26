using System;
using System.IO;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;

namespace Comfizen;

/// <summary>
/// A custom Serilog sink that forwards log events to the ConsoleLogService
/// for display in the application's UI console.
/// </summary>
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

        // Use a StringWriter to format the message template with its properties.
        using var writer = new StringWriter();
        _formatter.Format(logEvent, writer);

        // Get the formatted message text.
        var message = writer.ToString().TrimEnd('\r', '\n');
            
        // Convert Serilog level to our LogLevel.
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

        // MODIFIED: Use the new centralized method to log application messages
        _consoleLogService.LogApplicationMessage(message, level, ex: logEvent.Exception);
    }
}