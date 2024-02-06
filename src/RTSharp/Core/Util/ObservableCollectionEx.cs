using Avalonia.Controls;

using DynamicData;

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace RTSharp.Core.Util
{
    public class ObservableCollectionEx<T> : ObservableCollection<T>
    {
        public ObservableCollectionEx()
        {
        }

        public ObservableCollectionEx(ObservableCollectionEx<T> In) : base(In)
        {
        }

        public void Apply(SelectionChangedEventArgs e)
        {
            var c = this.Items.Count;

            foreach (var item in e.AddedItems) {
                this.Items.Add((T)item);
            }
            foreach (var item in e.RemovedItems) {
                this.Items.Remove((T)item);
            }

            if (c != this.Items.Count)
                this.OnPropertyChanged(new PropertyChangedEventArgs("Count"));

            this.OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            this.OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        public void Replace(IEnumerable<T> In)
        {
            var c = this.Items.Count;

            Items.Clear();
            foreach (var item in In)
                Items.Add(item);

            if (c != this.Items.Count)
                this.OnPropertyChanged(new PropertyChangedEventArgs("Count"));

            this.OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            this.OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        public void AddRange(IEnumerable<T> In)
        {
            var c = this.Items.Count;

            foreach (var item in In)
                Items.Add(item);

            if (c != this.Items.Count)
                this.OnPropertyChanged(new PropertyChangedEventArgs("Count"));

            this.OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            this.OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}
