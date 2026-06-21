namespace RTSharp.Shared.Abstractions;

public enum LogSeverity
{
    Unspecified,
    Debug,
    Info,
    Notice,
    Warning,
    Error,
    Critical
}

public record DataProviderDaemonLogEntry(DateTime Timestamp, LogSeverity Severity, string Message);
