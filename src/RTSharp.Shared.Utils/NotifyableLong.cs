using System.ComponentModel;

namespace RTSharp.Shared.Utils
{
	public class Notifyable<T> : INotifyPropertyChanged
	{
		public T Value { get; private set; }

		public event PropertyChangedEventHandler? PropertyChanged;

		public void Change(T Value)
		{
			if (Value != null && Value.Equals(this.Value))
				return;

			this.Value = Value;
			PropertyChanged?.Invoke(null, new PropertyChangedEventArgs(nameof(Value)));
		}
	}
}
