using System;
using System.ComponentModel;

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
            return $"{Connected} ({Total})";
        }

        public int CompareTo(ConnectedTotalPair other) => this.Connected.CompareTo(other.Connected);
    }
}
