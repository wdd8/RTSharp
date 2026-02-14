using System.ComponentModel;
using System.Reactive.Disposables;

namespace RTSharp.Shared.Utils
{
    public interface INotifyable
    {
        public string ValueStr { get; }
    }

    public class Notifyable<T> : INotifyPropertyChanged, INotifyable, IObservable<T>
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

        public IDisposable Subscribe(IObserver<T> observer)
        {
            PropertyChangedEventHandler fx = (object sender, PropertyChangedEventArgs e) => {
                observer.OnNext(Value);
            };

            PropertyChanged += fx;

            return Disposable.Create(() => PropertyChanged -= fx);
        }

        public override string ToString() => Value.ToString();
    }
}
