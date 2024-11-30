using System.ComponentModel;

namespace RTSharp.Shared.Utils
{
    public interface INotifyable
    {
        public string ValueStr { get; }
    }

    public class Notifyable<T> : INotifyPropertyChanged, INotifyable
    {
        public T Value { get; private set; }

        public string ValueStr => Value.ToString();

        public event PropertyChangedEventHandler? PropertyChanged;

        public void Change(T Value)
        {
            if (Value != null && Value.Equals(this.Value))
                return;

            this.Value = Value;
            PropertyChanged?.Invoke(null, new PropertyChangedEventArgs(nameof(Value)));
        }

        public override string ToString() => Value.ToString();
    }
}
