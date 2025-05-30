using Avalonia.Controls;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace RTSharp.Core.Util
{
    public class ObservableCollectionEx<T> : INotifyCollectionChanged, INotifyPropertyChanged, IList<T>, IList, IReadOnlyList<T>
    {
        public ObservableCollectionEx()
        {
        }

        private IList<T> Items = [];

        public int Count { get { return Items.Count; } }

        public bool IsReadOnly => Items.IsReadOnly;

        public bool IsFixedSize => false;

        public bool IsSynchronized => throw new NotImplementedException();

        public object SyncRoot => throw new NotImplementedException();

        T IList<T>.this[int index] { get => Items[index]; set => Items[index] = value; }
        object IList.this[int index] { get => Items[index]; set => Items[index] = (T)value; }

        public T this[int index] => Items[index];

        public event NotifyCollectionChangedEventHandler CollectionChanged;
        public event PropertyChangedEventHandler PropertyChanged;

        public void Apply(SelectionChangedEventArgs e)
        {
            var c = this.Items.Count;

            foreach (var item in e.AddedItems) {
                this.Items.Add((T)item);
            }
            foreach (var item in e.RemovedItems) {
                this.Items.Remove((T)item);
            }

            Notify(c);
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

            Notify(c);
        }

        public void Replace(List<T> In)
        {
            var c = this.Items.Count;

            Items = In;

            Notify(c);
        }

        public void Replace(ObservableCollectionEx<T> In)
        {
            var c = this.Items.Count;

            Items = In.Items;

            Notify(c);
        }

        public void Replace(IReadOnlyCollection<T> In)
        {
            var c = this.Items.Count;

            Items = [..In];

            Notify(c);
        }

        public void AddRange(IEnumerable<T> In)
        {
            var c = this.Items.Count;

            foreach (var item in In)
                Items.Add(item);

            Notify(c);
        }

        public void NotifyInnerItemsChanged()
        {
            CollectionChanged?.Invoke(this,new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        public void Sort(Comparison<T> comparison)
        {
            ((List<T>)Items).Sort(comparison);
            CollectionChanged?.Invoke(this,new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        private void Notify(int previousCount)
        {
            if (previousCount != this.Items.Count)
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));

            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        public int IndexOf(T item) => throw new NotImplementedException();
        public void Insert(int index, T item) => throw new NotImplementedException();
        public void RemoveAt(int index) => throw new NotImplementedException();
        public void Add(T item) => AddRange([ item ]);
        public void Clear()
        {
            var c = this.Items.Count;

            Items.Clear();

            Notify(c);
        }
        public bool Contains(T item) => throw new NotImplementedException();
        public void CopyTo(T[] array, int arrayIndex) => throw new NotImplementedException();
        public bool Remove(T item)
        {
            var c = this.Items.Count;

            var ret = Items.Remove(item);

            Notify(c);

            return ret;
        }
        public IEnumerator<T> GetEnumerator() => Items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public int Add(object value) => throw new NotImplementedException();
        public bool Contains(object value) => throw new NotImplementedException();
        public int IndexOf(object value) => throw new NotImplementedException();
        public void Insert(int index, object value) => throw new NotImplementedException();
        public void Remove(object value) => throw new NotImplementedException();
        public void CopyTo(Array array, int index) => throw new NotImplementedException();
    }
}
