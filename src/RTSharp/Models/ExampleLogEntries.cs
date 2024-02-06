using System;
using System.Collections.ObjectModel;
using Serilog.Events;

namespace RTSharp.Models;

public class ExampleLogEntries
{
    public ObservableCollection<LogEntry> Entries { get; set; }
    
    public ExampleLogEntries()
    {
        Entries = new ObservableCollection<LogEntry>() {
            new LogEntry(LogEventLevel.Verbose, DateTime.Parse("2022-03-05 01:01"), "Test"),
            new LogEntry(LogEventLevel.Debug, DateTime.Parse("2022-03-05 01:01"), "Test"),
            new LogEntry(LogEventLevel.Information, DateTime.Parse("2022-03-05 01:01"), "Test"),
            new LogEntry(LogEventLevel.Warning, DateTime.Parse("2022-03-05 01:01"), "Test"),
            new LogEntry(LogEventLevel.Error, DateTime.Parse("2022-03-05 01:01"), "Test"),
            new LogEntry(LogEventLevel.Fatal, DateTime.Parse("2022-03-05 01:01"), "Test")
        };
    }
}