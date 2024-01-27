using System;
using System.Collections.Generic;
using System.Text;

using CommunityToolkit.Mvvm.ComponentModel;

namespace RTSharp.Models
{
    public partial class ConnectedTotalPair : ObservableObject
    {
        [ObservableProperty]
        public uint connected;

        [ObservableProperty]
        public uint total;

        public ConnectedTotalPair(uint Connected, uint Total)
        {
            this.Connected = Connected;
            this.Total = Total;
        }

        public override string ToString()
        {
            return $"{Connected} ({Total})";
        }
    }
}
