using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace RTSharp.Core;

public class ObservableRangeCollection<T> : ObservableCollection<T>
{
    public ObservableRangeCollection()
    {
    }

    public ObservableRangeCollection(IEnumerable<T> items)
        : base(items)
    {
    }

    public void AddRange(IEnumerable<T> items)
    {
        InsertRange(Count, items);
    }

    public void InsertRange(int index, IEnumerable<T> items)
    {
        if (items == null) {
            throw new ArgumentNullException(nameof(items));
        }

        var materialized = Materialize(items);
        if (materialized.Count == 0) {
            return;
        }

        CheckReentrancy();

        if (Items is List<T> list) {
            list.InsertRange(index, materialized);
        } else {
            for (var i = 0;i < materialized.Count;i++) {
                Items.Insert(index + i, materialized[i]);
            }
        }

        RaiseChange(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Add,
            (IList)materialized,
            index));
    }

    public void RemoveRange(int index, int count)
    {
        if (count <= 0) {
            return;
        }

        if (index < 0 || index + count > Count) {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        CheckReentrancy();

        IList<T> removed;
        if (Items is List<T> list) {
            removed = list.GetRange(index, count);
            list.RemoveRange(index, count);
        } else {
            var buffer = new List<T>(count);
            for (var i = 0;i < count;i++) {
                buffer.Add(Items[index]);
                Items.RemoveAt(index);
            }

            removed = buffer;
        }

        RaiseChange(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Remove,
            (IList)removed,
            index));
    }

    public void RemoveRange(IList<T> items)
    {
        CheckReentrancy();

        var removed = new List<T>(items.Count);

        var list = (List<T>)Items;

        for (var x = 0;x < list.Count;x++) {
            foreach (var item in items) {
                if (Object.Equals(list[x], item)) {
                    Items.RemoveAt(x);
                    removed.Add(item);
                }
            }
        }

        RaiseChange(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Remove,
            removed));
    }

    public void ResetWith(IEnumerable<T> items)
    {
        if (items == null) {
            throw new ArgumentNullException(nameof(items));
        }

        CheckReentrancy();

        Items.Clear();
        foreach (var item in items) {
            Items.Add(item);
        }

        RaiseReset();
    }

    private void RaiseChange(NotifyCollectionChangedEventArgs args)
    {
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(args);
    }

    private void RaiseReset()
    {
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    private static IList<T> Materialize(IEnumerable<T> items)
    {
        if (items is IList<T> list) {
            return list;
        }

        return items.ToList();
    }
}
