using System;
using Serilog.Events;

namespace RTSharp.Models
{
    public sealed record LogEntry(LogEventLevel LogLevel, DateTime When, string Message, Exception? Exception = null)
    {
        public bool Equals(LogEntry? other) => this.When == other.When;

        public override int GetHashCode()
        {
            unchecked { return (int)this.When.Ticks; }
        }

        public bool HasException => Exception != null;
    }
}
