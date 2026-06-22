using System;
using System.ComponentModel;
using System.Globalization;

namespace RTSharp.Models
{
    public record struct ConnectedTotalPair : IComparable<ConnectedTotalPair>
    {
        public uint Connected { get; set; }

        public uint Total { get; set; }

        public ConnectedTotalPair(uint Connected, uint Total)
        {
            this.Connected = Connected;
            this.Total = Total;
        }

        public override string ToString()
        {
            Span<char> buf = stackalloc char[32];
            if (buf.TryWrite(CultureInfo.InvariantCulture, $"{Connected} ({Total})", out int written))
                return new string(buf[..written]);

            return $"{Connected} ({Total})";
        }

        public int CompareTo(ConnectedTotalPair other) => this.Connected.CompareTo(other.Connected);
    }
}
