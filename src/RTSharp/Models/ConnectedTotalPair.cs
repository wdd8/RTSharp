using System.ComponentModel;

namespace RTSharp.Models
{
    public record struct ConnectedTotalPair : INotifyPropertyChanged
    {
        public uint Connected {
            get => field;
            set {
                var old = field;
                field = value;
                if (old != value)
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Connected)));
            }
        }

        public uint Total {
            get => field;
            set {
                var old = field;
                field = value;
                if (old != value)
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Total)));
            }
        }

        public ConnectedTotalPair(uint Connected, uint Total)
        {
            this.Connected = Connected;
            this.Total = Total;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public override string ToString()
        {
            return $"{Connected} ({Total})";
        }
    }
}
