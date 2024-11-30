using Avalonia.Controls;

using System;
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

            this.OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        public void Apply(NotifyCollectionChangedEventArgs e)
        {
            var c = this.Items.Count;

            if (c == 0)
                return;

            if (e.OldItems != null) {
                foreach (var item in e.OldItems) {
                    this.Items.Remove((T)item);
                }
            }
            if (e.NewItems != null) {
                foreach (var item in e.NewItems) {
                    this.Items.Add((T)item);
                }
            }

            if (c != this.Items.Count)
                this.OnPropertyChanged(new PropertyChangedEventArgs("Count"));

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

        public void Replace(System.Collections.IEnumerable In)
        {
            var c = this.Items.Count;

            Items.Clear();
            foreach (var item in In)
                Items.Add((T)item);

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

            this.OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        public void NotifyInnerItemsChanged()
        {
            this.OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        public void Sort(Comparison<T> comparison)
        {
            ((List<T>)Items).Sort(comparison);
            this.OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            this.OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}
