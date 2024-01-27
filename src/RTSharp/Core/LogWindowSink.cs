using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using RTSharp.Models;
using Serilog.Core;
using Serilog.Events;

namespace RTSharp.Core
{
    public class LogWindowSink : ILogEventSink
    {
	    private readonly IFormatProvider? FormatProvider;
	    public static ObservableCollection<LogEntry> LogEntries { get; } = new ObservableCollection<LogEntry>();

        public LogWindowSink(IFormatProvider? FormatProvider)
        {
	        this.FormatProvider = FormatProvider;
        }

        public void Emit(LogEvent logEvent)
        {
            var message = logEvent.RenderMessage(FormatProvider);

			if (logEvent.Exception != null)
                message += "\n" + logEvent.Exception.Message;

            Dispatcher.UIThread.InvokeAsync(() => LogEntries.Add(new LogEntry(logEvent.Level, DateTime.Now, message, logEvent.Exception)));
        }
    }
}
