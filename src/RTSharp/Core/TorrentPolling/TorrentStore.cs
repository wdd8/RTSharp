using Avalonia.Threading;

using RTSharp.Models;
using RTSharp.Plugin;
using RTSharp.Shared.Abstractions;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace RTSharp.Core.TorrentPolling;

public record class TorrentStoreChangeSet
{
    public TorrentStoreChangeSet(
        IReadOnlyList<Models.Torrent> Added,
        IReadOnlyList<Models.Torrent> Refreshed,
        IReadOnlyList<Models.Torrent> Removed,
        RTSharpDataProvider DataProvider)
    {
        this.Added = Added;
        this.Refreshed = Refreshed;
        this.Removed = Removed;
        this.DataProvider = DataProvider;
    }

    public IReadOnlyList<Models.Torrent> Added { get; }
    public IReadOnlyList<Models.Torrent> Refreshed { get; }
    public IReadOnlyList<Models.Torrent> Removed { get; }
    public RTSharpDataProvider DataProvider { get; }

    public bool IsEmpty => Added.Count == 0 && Refreshed.Count == 0 && Removed.Count == 0;
}

public sealed class TorrentStore
{
    private const int ResetThreshold = 500;

    // reentrant lock
    private readonly object Lock = new();
    private readonly Dictionary<GlobalTorrentKey, Models.Torrent> InternalDict = new(new GlobalTorrentKeyComparer());
    private List<Models.Torrent> PendingAdded = new();
    private List<Models.Torrent> PendingRefreshed = new();
    private List<Models.Torrent> PendingRemoved = new();

    private readonly Dictionary<Models.Torrent, (ulong DL, ulong UP)> CountedSpeeds = new(ReferenceEqualityComparer.Instance);
    private ulong TotalDLSpeed;
    private ulong TotalUPSpeed;
#if DEBUG
    private bool InEdit;
#endif

    private readonly VisibleCollection Visible = new();
    private readonly ReadOnlyObservableCollection<Models.Torrent> _readOnly;
    private Func<Models.Torrent, bool> FxFilter = static _ => true;
    private IComparer<Models.Torrent> SortComparer = Comparer<Models.Torrent>.Create(static (_, _) => 0);
    private bool HasSort;

    public event EventHandler<TorrentStoreChangeSet>? Changed;

    public TorrentStore()
    {
        _readOnly = new ReadOnlyObservableCollection<Models.Torrent>(Visible);
    }

    public ReadOnlyObservableCollection<Models.Torrent> VisibleItems => _readOnly;

    public List<Models.Torrent> GetSnapshot()
    {
        lock (Lock) {
            var snapshot = new List<Models.Torrent>(InternalDict.Count);
            snapshot.AddRange(InternalDict.Values);
            return snapshot;
        }
    }

    public (ulong DLSpeed, ulong UPSpeed) GetTotalSpeeds()
    {
        lock (Lock) {
            return (TotalDLSpeed, TotalUPSpeed);
        }
    }

    private void ApplyTotals(TorrentStoreChangeSet changes)
    {
        lock (Lock) {
            foreach (var torrent in changes.Removed) {
                if (CountedSpeeds.Remove(torrent, out var counted)) {
                    TotalDLSpeed -= counted.DL;
                    TotalUPSpeed -= counted.UP;
                }
            }

            foreach (var torrent in changes.Added)
                CountTorrent(torrent);
            foreach (var torrent in changes.Refreshed)
                CountTorrent(torrent);

#if DEBUG
            VerifyTotals();
#endif
        }
    }

    private void CountTorrent(Models.Torrent torrent)
    {
        var pending = torrent.PendingChanges;
        var dl = pending == null ? torrent.DLSpeed : pending.DLSpeed;
        var up = pending == null ? torrent.UPSpeed : pending.UPSpeed;

        ref var counted = ref CollectionsMarshal.GetValueRefOrAddDefault(CountedSpeeds, torrent, out var exists);

        if (exists) {
            TotalDLSpeed += dl - counted.DL;
            TotalUPSpeed += up - counted.UP;
        } else {
            TotalDLSpeed += dl;
            TotalUPSpeed += up;
        }

        counted = (dl, up);
    }

    private void ResyncTotals()
    {
        lock (Lock) {
            CountedSpeeds.Clear();

            ulong dl = 0, up = 0;
            foreach (var torrent in InternalDict.Values) {
                var pending = torrent.PendingChanges;
                var speeds = pending == null ? (torrent.DLSpeed, torrent.UPSpeed) : (pending.DLSpeed, pending.UPSpeed);

                CountedSpeeds[torrent] = speeds;
                dl += speeds.DLSpeed;
                up += speeds.UPSpeed;
            }

            TotalDLSpeed = dl;
            TotalUPSpeed = up;
        }
    }

#if DEBUG
    private void VerifyTotals()
    {
        ulong dl = 0, up = 0;
        foreach (var torrent in InternalDict.Values) {
            var pending = torrent.PendingChanges;
            dl += pending == null ? torrent.DLSpeed : pending.DLSpeed;
            up += pending == null ? torrent.UPSpeed : pending.UPSpeed;
        }

        /*Debug.Assert(TotalDLSpeed == dl, $"Incremental DL total {TotalDLSpeed} != actual {dl}");
        Debug.Assert(TotalUPSpeed == up, $"Incremental UP total {TotalUPSpeed} != actual {up}");*/
        Debug.Assert(CountedSpeeds.Count == InternalDict.Count, $"Counted {CountedSpeeds.Count} torrents != stored {InternalDict.Count}");
    }
#endif

    public bool TryGet(GlobalTorrentKey key, [MaybeNullWhen(false)] out Models.Torrent torrent)
    {
        lock (Lock) {
            return InternalDict.TryGetValue(key, out torrent);
        }
    }

    public void ApplySort(IComparer<Models.Torrent> comparer, bool hasSort)
    {
        SortComparer = comparer ?? Comparer<Models.Torrent>.Create(static (_, _) => 0);
        HasSort = hasSort;
        Rebuild();
    }

    public void ApplyFilter(Func<Models.Torrent, bool> predicate)
    {
        FxFilter = predicate ?? (static _ => true);
        Rebuild();
    }

    public void Edit(RTSharpDataProvider Dp, Action<TorrentStore> Update)
    {
#if DEBUG
        if (InEdit)
            throw new InvalidOperationException("Nested edits are not allowed.");
        InEdit = true;
#endif

        TorrentStoreChangeSet changeSet;
        lock (Lock) {
            Update(this);

            changeSet = new TorrentStoreChangeSet(PendingAdded, PendingRefreshed, PendingRemoved, Dp);
            PendingAdded = [];
            PendingRefreshed = [];
            PendingRemoved = [];

            ApplyTotals(changeSet);
        }
#if DEBUG
        InEdit = false;
#endif

        if (changeSet.IsEmpty)
            return;

        Dispatcher.UIThread.Post(() => {
            lock (Lock) {
                foreach (var t in changeSet.Added)
                    t.CommitUIChanges();
                foreach (var t in changeSet.Refreshed)
                    t.CommitUIChanges();
            }

            Changed?.Invoke(this, changeSet);
            ApplyChanges(changeSet);
        });
    }

    public void Add(Models.Torrent torrent)
    {
        ArgumentNullException.ThrowIfNull(torrent);
#if DEBUG
        if (!InEdit)
            throw new InvalidOperationException("Must be in edit");
#endif

        InternalDict.Add(GetKey(torrent), torrent);
        PendingAdded.Add(torrent);
    }

    public void RemoveKeys(IEnumerable<GlobalTorrentKey> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
#if DEBUG
        if (!InEdit)
            throw new InvalidOperationException("Must be in edit");
#endif

        foreach (var key in keys) {
            if (InternalDict.Remove(key, out var removed))
                PendingRemoved.Add(removed);
        }
    }

    public void Refresh(Models.Torrent torrent)
    {
        ArgumentNullException.ThrowIfNull(torrent);
#if DEBUG
        if (!InEdit)
            throw new InvalidOperationException("Must be in edit");
#endif

        if (InternalDict.ContainsKey(GetKey(torrent)))
            PendingRefreshed.Add(torrent);
    }

    private void ApplyChanges(TorrentStoreChangeSet changes)
    {
        var total = changes.Added.Count + changes.Refreshed.Count + changes.Removed.Count;
        if (total >= ResetThreshold) {
            Rebuild();
            return;
        }

        List<Models.Torrent>? pendingMoves = null;

        Visible.BeginBatch(); {
            var removed = (List<Models.Torrent>)changes.Removed;

            // Evaluate filter on Refreshed so they sort and execute with explicit removes
            foreach (var t in changes.Refreshed) {
                if (t.VisibleIndex >= 0 && !FxFilter(t))
                    removed.Add(t);
            }

            /*
             * Sort descending so that each removal leaves earlier indices untouched, keeping
             * VisibleIndex values accurate for subsequent removals without re-indexing
             */
            if (removed.Count > 1)
                removed.Sort((a, b) => b.VisibleIndex.CompareTo(a.VisibleIndex));

            foreach (var t in removed)
                RemoveVisible(t);
            foreach (var t in changes.Added) {
                if (!FxFilter(t))
                    continue;

                AddVisible(t);
            }

            // Add to visible if refreshed item now passed the filter
            foreach (var t in changes.Refreshed) {
                if (t.VisibleIndex < 0) {
                    if (FxFilter(t))
                        AddVisible(t);
                } else
                    (pendingMoves ??= []).Add(t);
            }
        } Visible.EndBatch();

        if (pendingMoves != null && HasSort) {
            foreach (var t in pendingMoves) {
                var idx = t.VisibleIndex;

                var newIdx = Visible.BinarySearchSkipping(t, idx, SortComparer);
                if (newIdx != idx)
                    Visible.Move(idx, newIdx);
            }
        }
    }

    private void Rebuild()
    {
        ResyncTotals();

        var snapshot = GetSnapshot();
        var visible = new List<Models.Torrent>(snapshot.Count);
        foreach (var item in snapshot) {
            if (FxFilter(item))
                visible.Add(item);
        }

        if (HasSort)
            visible.Sort(SortComparer);
        Visible.ResetWith(visible);
    }

    private void AddVisible(Models.Torrent torrent)
    {
        if (!HasSort) {
            Visible.Add(torrent);
        } else {
            var pos = Visible.BinarySearch(torrent, SortComparer);
            Visible.Insert(pos < 0 ? ~pos : pos, torrent);
        }
    }

    private void RemoveVisible(Models.Torrent torrent)
    {
        var index = torrent.VisibleIndex;
        if (index >= 0)
            Visible.RemoveAt(index);
    }

    internal static GlobalTorrentKey GetKey(Models.Torrent torrent) => new(torrent.Hash, torrent.DataOwner.Host.InstanceId);

    internal sealed class GlobalTorrentKeyComparer : IEqualityComparer<GlobalTorrentKey>
    {
        public bool Equals(GlobalTorrentKey x, GlobalTorrentKey y)
            => x.DataProviderInstanceId == y.DataProviderInstanceId && HashEqualityComparer.Instance.Equals(x.InfoHash, y.InfoHash);

        public int GetHashCode(GlobalTorrentKey obj)
            => HashCode.Combine(obj.DataProviderInstanceId, HashEqualityComparer.Instance.GetHashCode(obj.InfoHash));
    }

    private sealed class VisibleCollection : ObservableCollection<Models.Torrent>
    {
        private bool InBatch;

        public int BinarySearch(Models.Torrent item, IComparer<Models.Torrent> comparer) => ((List<Models.Torrent>)Items).BinarySearch(item, comparer);

        public int BinarySearchSkipping(Models.Torrent item, int skipIndex, IComparer<Models.Torrent> comparer)
        {
            var list = (List<Models.Torrent>)Items;

            if (skipIndex > 0) {
                var leftResult = list.BinarySearch(0, skipIndex, item, comparer);
                var leftInsert = leftResult >= 0 ? leftResult : ~leftResult;
                if (leftInsert < skipIndex) return leftInsert;
            }

            if (skipIndex < this.Count - 1) {
                var rightStart = skipIndex + 1;
                var rightResult = list.BinarySearch(rightStart, Count - rightStart, item, comparer);
                var rightInsert = rightResult >= 0 ? rightResult : ~rightResult;
                return rightInsert - 1;
            }

            return skipIndex;
        }

        public void BeginBatch() => InBatch = true;

        public void EndBatch()
        {
            InBatch = false;
            var list = (List<Models.Torrent>)Items;
            for (var i = 0; i < list.Count; i++)
                list[i].VisibleIndex = i;
        }

        public void ResetWith(IReadOnlyList<Models.Torrent> items)
        {
            foreach (var t in (List<Models.Torrent>)Items)
                t.VisibleIndex = -1;

            Items.Clear();
            for (var i = 0; i < items.Count; i++) {
                items[i].VisibleIndex = i;
                Items.Add(items[i]);
            }

            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        protected override void InsertItem(int index, Models.Torrent item)
        {
            base.InsertItem(index, item);
            if (InBatch)
                return;

            var list = (List<Models.Torrent>)Items;
            for (var i = index; i < list.Count; i++)
                list[i].VisibleIndex = i;
        }

        protected override void RemoveItem(int index)
        {
            this[index].VisibleIndex = -1;
            base.RemoveItem(index);
            if (InBatch)
                return;

            var list = (List<Models.Torrent>)Items;
            for (var i = index; i < list.Count; i++)
                list[i].VisibleIndex = i;
        }

        protected override void SetItem(int index, Models.Torrent item)
        {
            this[index].VisibleIndex = -1;
            base.SetItem(index, item);
            item.VisibleIndex = index;
        }

        protected override void MoveItem(int oldIndex, int newIndex)
        {
            base.MoveItem(oldIndex, newIndex);
            var low = Math.Min(oldIndex, newIndex);
            var high = Math.Max(oldIndex, newIndex);
            var list = (List<Models.Torrent>)Items;
            for (var i = low; i <= high; i++)
                list[i].VisibleIndex = i;
        }

        protected override void ClearItems()
        {
            foreach (var t in (List<Models.Torrent>)Items)
                t.VisibleIndex = -1;
            base.ClearItems();
        }
    }
}
