using DynamicData;
using DynamicData.Binding;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;

namespace RTSharp.ViewModels
{
    public static class RxExtensions
    {
        private static IObservable<IChangeSet<TObject>> AutoRefreshOnObservable<TObject, TAny>(IObservable<IChangeSet<TObject>> source, Func<TObject, IObservable<TAny>> reevaluator)
            where TObject : notnull
        {
            return new FastAutoRefreshImpl<TObject, TAny>(source, reevaluator).Run();
        }

        private sealed class FastAutoRefreshImpl<TObject, TAny>(IObservable<IChangeSet<TObject>> source, Func<TObject, IObservable<TAny>> reEvaluator)
            where TObject : notnull
        {
            private readonly Func<TObject, IObservable<TAny>> _reEvaluator = reEvaluator ?? throw new ArgumentNullException(nameof(reEvaluator));
            private readonly IObservable<IChangeSet<TObject>> _source = source ?? throw new ArgumentNullException(nameof(source));

            public IObservable<IChangeSet<TObject>> Run() => Observable.Create<IChangeSet<TObject>>(
                    observer => {
                        var locker = new Lock();

                        var allItems = new List<TObject>();

                        var shared = _source.Synchronize(locker).Clone(allItems) // clone all items so we can look up the index when a change has been made
                            .Publish();

                        // monitor each item observable and create change
                        var itemHasChanged = shared.MergeMany((t) => _reEvaluator(t).Select(_ => t));

                        IObservable<IChangeSet<TObject>> requiresRefresh = itemHasChanged.Synchronize(locker).Select(
                            item => // catch all the indices of items which have been refreshed
                                new ChangeSet<TObject>([new Change<TObject>(ListChangeReason.Refresh, item, allItems.IndexOf(item))]));

                        // publish refreshes and underlying changes
                        var publisher = shared.Merge(requiresRefresh).SubscribeSafe(observer);

                        return new CompositeDisposable(publisher, shared.Connect());
                    });
        }

        public static IObservable<IChangeSet<TObject>> FastAutoRefresh<TObject>(this IObservable<IChangeSet<TObject>> source)
            where TObject : INotifyPropertyChanged
        {
            return AutoRefreshOnObservable(source,
                t => {
                    return t.WhenAnyPropertyChanged();
                });
        }

        private class RefreshingObservableCollectionAdaptor<T>(IObservableCollection<T> collection, int refreshThreshold, bool allowReplace = true, bool resetOnFirstTimeLoad = true) : IChangeSetAdaptor<T>
            where T : notnull
        {
            private readonly IObservableCollection<T> _collection = collection ?? throw new ArgumentNullException(nameof(collection));
            private bool _loaded;

            /// <summary>
            /// Initializes a new instance of the <see cref="ObservableCollectionAdaptor{TObject}"/> class.
            /// </summary>
            /// <param name="collection">The collection.</param>
            public RefreshingObservableCollectionAdaptor(IObservableCollection<T> collection)
                : this(collection, DynamicDataOptions.Binding)
            {
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="ObservableCollectionAdaptor{TObject}"/> class.
            /// </summary>
            /// <param name="collection">The collection.</param>
            /// <param name="options"> The binding options.</param>
            public RefreshingObservableCollectionAdaptor(IObservableCollection<T> collection, BindingOptions options)
                : this(collection, options.ResetThreshold, options.UseReplaceForUpdates, options.ResetOnFirstTimeLoad)
            {
            }

            /// <summary>
            /// Maintains the specified collection from the changes.
            /// </summary>
            /// <param name="changes">The changes.</param>
            public void Adapt(IChangeSet<T> changes)
            {
                if (changes.TotalChanges - changes.Refreshes > refreshThreshold || (!_loaded && resetOnFirstTimeLoad)) {
                    using (_collection.SuspendNotifications()) {
                        RefreshingClone(_collection, changes, null);
                        _loaded = true;
                    }
                } else {
                    // TODO: pass in allowReplace to handle replace vs remove / add
                    RefreshingClone(_collection, changes, null);
                }
            }
        }

        private static void RefreshingClone<T>(IList<T> source, IEnumerable<Change<T>> changes, IEqualityComparer<T>? equalityComparer)
            where T : notnull
        {
            foreach (var item in changes) {
                RefreshingClone(source, item, equalityComparer ?? EqualityComparer<T>.Default);
            }
        }

        private static void ClearOrRemoveMany<T>(IList<T> source, Change<T> change)
            where T : notnull
        {
            // apply this to other operators
            if (source.Count == change.Range.Count) {
                source.Clear();
            } else {
                source.RemoveMany(change.Range);
            }
        }

        private static void RemoveRange<T>(IList<T> source, int index, int count)
        {
            switch (source) {
                case List<T> list:
                    list.RemoveRange(index, count);
                    break;
                case IExtendedList<T> list:
                    list.RemoveRange(index, count);
                    break;
                default:
                    throw new NotSupportedException($"Cannot remove range from {source.GetType().FullName}");
            }
        }

        private static void RefreshingClone<T>(IList<T> source, Change<T> item, IEqualityComparer<T> equalityComparer)
            where T : notnull
        {
            var changeAware = source as ChangeAwareList<T>;

            switch (item.Reason) {
                case ListChangeReason.Add: {
                    var change = item.Item;
                    var hasIndex = change.CurrentIndex >= 0;
                    if (hasIndex) {
                        source.Insert(change.CurrentIndex, change.Current);
                    } else {
                        source.Add(change.Current);
                    }

                    break;
                }

                case ListChangeReason.AddRange: {
                    source.AddOrInsertRange(item.Range, item.Range.Index);
                    break;
                }

                case ListChangeReason.Clear: {
                    ClearOrRemoveMany(source, item);
                    break;
                }

                case ListChangeReason.Replace: {
                    var change = item.Item;
                    if (change.CurrentIndex >= 0 && change.CurrentIndex == change.PreviousIndex) {
                        source[change.CurrentIndex] = change.Current;
                    } else {
                        if (change.PreviousIndex == -1) {
                            source.Remove(change.Previous.Value);
                        } else {
                            // is this best? or replace + move?
                            source.RemoveAt(change.PreviousIndex);
                        }

                        if (change.CurrentIndex == -1) {
                            source.Add(change.Current);
                        } else {
                            source.Insert(change.CurrentIndex, change.Current);
                        }
                    }

                    break;
                }

                case ListChangeReason.Refresh: {
                    if (changeAware is not null) {
                        changeAware.RefreshAt(item.Item.CurrentIndex);
                    } else {
                        source[item.Item.CurrentIndex] = item.Item.Current;
                    }

                    break;
                }

                case ListChangeReason.Remove: {
                    var change = item.Item;
                    var hasIndex = change.CurrentIndex >= 0;
                    if (hasIndex) {
                        source.RemoveAt(change.CurrentIndex);
                    } else {
                        var index = source.IndexOf(change.Current, equalityComparer);
                        if (index > -1) {
                            source.RemoveAt(index);
                        }
                    }

                    break;
                }

                case ListChangeReason.RemoveRange: {
                    // ignore this case because WhereReasonsAre removes the index [in which case call RemoveMany]
                    //// if (item.Range.Index < 0)
                    ////    throw new UnspecifiedIndexException("ListChangeReason.RemoveRange should not have an index specified index");
                    if (item.Range.Index >= 0 && (source is IExtendedList<T> || source is List<T>)) {
                        RemoveRange(source, item.Range.Index, item.Range.Count);
                    } else {
                        source.RemoveMany(item.Range);
                    }

                    break;
                }

                case ListChangeReason.Moved: {
                    var change = item.Item;
                    var hasIndex = change.CurrentIndex >= 0;
                    if (!hasIndex) {
                        throw new UnspecifiedIndexException("Cannot move as an index was not specified");
                    }

                    if (source is IExtendedList<T> extendedList) {
                        extendedList.Move(change.PreviousIndex, change.CurrentIndex);
                    } else if (source is ObservableCollection<T> observableCollection) {
                        observableCollection.Move(change.PreviousIndex, change.CurrentIndex);
                    } else {
                        // check this works whatever the index is
                        source.RemoveAt(change.PreviousIndex);
                        source.Insert(change.CurrentIndex, change.Current);
                    }

                    break;
                }
            }
        }

        public static IObservable<IChangeSet<T>> RefreshingBind<T>(this IObservable<IChangeSet<T>> source, out ReadOnlyObservableCollection<T> readOnlyObservableCollection, BindingOptions options)
            where T : notnull
        {
            var target = new ObservableCollectionExtended<T>();
            var result = new ReadOnlyObservableCollection<T>(target);
            var adaptor = new RefreshingObservableCollectionAdaptor<T>(target, options);
            readOnlyObservableCollection = result;
            return source.Adapt(adaptor);
        }
    }
}
