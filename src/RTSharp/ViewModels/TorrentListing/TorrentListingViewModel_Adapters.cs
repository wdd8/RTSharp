using Avalonia.Controls;
using Avalonia.Controls.DataGridFiltering;
using Avalonia.Controls.DataGridSorting;

using RTSharp.Models;

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace RTSharp.ViewModels.TorrentListing
{
    public class TorrentSortingAdapterFactory : IDataGridSortingAdapterFactory
    {
        private readonly Action<IComparer<Torrent>, bool> FxApply;
        private Func<IEnumerable<DataGridColumn>>? _columnProvider;
        public IComparer<Torrent> SortComparer { get; private set; }
        public bool HasSort { get; private set; }

        public TorrentSortingAdapterFactory(Action<IComparer<Torrent>, bool> apply)
        {
            SortComparer = Comparer<Torrent>.Create(static (_, _) => 0);
            FxApply = apply ?? throw new ArgumentNullException(nameof(apply));
        }

        public DataGridSortingAdapter Create(DataGrid grid, ISortingModel model)
        {
            _columnProvider = () => grid.ColumnDefinitions;
            return new DynamicDataSortingAdapter(model, _columnProvider, UpdateComparer);
        }

        public void UpdateComparer(IReadOnlyList<SortingDescriptor> descriptors)
        {
            SortComparer = BuildComparer(descriptors);
            HasSort = descriptors is { Count: > 0 };
            FxApply(SortComparer, HasSort);
        }

        private IComparer<Torrent> BuildComparer(IReadOnlyList<SortingDescriptor> descriptors)
        {
            if (descriptors == null || descriptors.Count == 0) {
                return Comparer<Torrent>.Create(static (_, _) => 0);
            }

            var columns = _columnProvider?.Invoke()?.ToList();

            var compiled = descriptors
                .Where(x => x != null)
                .Select(d => Compile(d, columns))
                .Where(x => x != null)
                .ToList();

            if (compiled.Count == 0) {
                return Comparer<Torrent>.Create(static (_, _) => 0);
            } else if (compiled.Count == 1) {
                var comparer = compiled[0];
                return Comparer<Torrent>.Create((left, right) => {
                    var result = comparer!.Compare(left, right);

                    if (result != 0) {
                        return comparer.Direction == ListSortDirection.Ascending ? result : -result;
                    }

                    return 0;
                });
            } else {
                return Comparer<Torrent>.Create((left, right) => {
                    foreach (var entry in compiled) {
                        var result = entry!.Compare(left, right);

                        if (result != 0) {
                            return entry.Direction == ListSortDirection.Ascending ? result : -result;
                        }
                    }

                    return 0;
                });
            }
        }

        private static CompiledComparer? Compile(SortingDescriptor descriptor, IList<DataGridColumn>? columns)
        {
            if (descriptor.Comparer == null) {
                return null;
            }

            var column = columns?.FirstOrDefault(c => Equals(c.ColumnKey, (descriptor.ColumnId as DataGridColumn)?.ColumnKey ?? descriptor.ColumnId));
            var accessor = column != null ? DataGridColumnMetadata.GetValueAccessor(column) : null;

            if (accessor == null) {
                return null;
            }

            return (accessor, descriptor.Comparer) switch {
                (IDataGridColumnValueAccessor<Torrent, string> a, IComparer<string> c) =>
                    new CompiledComparer((l, r) => c.Compare(a.GetValue(l), a.GetValue(r)), descriptor.Direction),
                (IDataGridColumnValueAccessor<Torrent, ulong> a, IComparer<ulong> c) =>
                    new CompiledComparer((l, r) => c.Compare(a.GetValue(l), a.GetValue(r)), descriptor.Direction),
                (IDataGridColumnValueAccessor<Torrent, uint> a, IComparer<uint> c) =>
                    new CompiledComparer((l, r) => c.Compare(a.GetValue(l), a.GetValue(r)), descriptor.Direction),
                (IDataGridColumnValueAccessor<Torrent, float> a, IComparer<float> c) =>
                    new CompiledComparer((l, r) => c.Compare(a.GetValue(l), a.GetValue(r)), descriptor.Direction),
                (IDataGridColumnValueAccessor<Torrent, DateTime> a, IComparer<DateTime> c) =>
                    new CompiledComparer((l, r) => c.Compare(a.GetValue(l), a.GetValue(r)), descriptor.Direction),
                (IDataGridColumnValueAccessor<Torrent, DateTime?> a, IComparer<DateTime?> c) =>
                    new CompiledComparer((l, r) => c.Compare(a.GetValue(l), a.GetValue(r)), descriptor.Direction),
                (IDataGridColumnValueAccessor<Torrent, TimeSpan> a, IComparer<TimeSpan> c) =>
                    new CompiledComparer((l, r) => c.Compare(a.GetValue(l), a.GetValue(r)), descriptor.Direction),
                (IDataGridColumnValueAccessor<Torrent, byte[]> a, IComparer<byte[]> c) =>
                    new CompiledComparer((l, r) => c.Compare(a.GetValue(l), a.GetValue(r)), descriptor.Direction),
                _ => null
            };
        }

        private sealed class DynamicDataSortingAdapter(ISortingModel model, Func<IEnumerable<DataGridColumn>> columns, Action<IReadOnlyList<SortingDescriptor>> FxUpdate) : DataGridSortingAdapter(model, columns)
        {
            protected override bool TryApplyModelToView(
                IReadOnlyList<SortingDescriptor> descriptors,
                IReadOnlyList<SortingDescriptor> previousDescriptors,
                out bool changed)
            {
                FxUpdate(descriptors);
                changed = true;
                return true;
            }
        }

        private record CompiledComparer(Func<Torrent, Torrent, int> Compare, ListSortDirection Direction);
    }

    public class TorrentFilteringAdapterFactory : IDataGridFilteringAdapterFactory
    {
        private readonly Action<Func<Torrent, bool>> FxApply;

        public Func<Torrent, bool> FilterPredicate { get; private set; }

        public TorrentFilteringAdapterFactory(Action<Func<Torrent, bool>> apply)
        {
            FilterPredicate = static _ => true;
            FxApply = apply ?? throw new ArgumentNullException(nameof(apply));
        }

        public DataGridFilteringAdapter Create(DataGrid grid, IFilteringModel model) => new DynamicDataFilteringAdapter(model, () => grid.ColumnDefinitions, UpdateFilter);

        public void UpdateFilter(IReadOnlyList<FilteringDescriptor> descriptors)
        {
            FilterPredicate = BuildPredicate(descriptors);
            FxApply(FilterPredicate);
        }

        private static Func<Torrent, bool> BuildPredicate(IReadOnlyList<FilteringDescriptor> descriptors)
        {
            if (descriptors == null || descriptors.Count == 0) {
                return AlwaysTrue;
            }

            var compiled = new List<Func<Torrent, bool>>(descriptors.Count);
            foreach (var descriptor in descriptors) {
                var predicate = Compile(descriptor);
                if (predicate != null) {
                    compiled.Add(predicate);
                }
            }

            if (compiled.Count == 0) {
                return AlwaysTrue;
            }

            if (compiled.Count == 1) {
                return compiled[0];
            }

            return item => {
                for (int i = 0;i < compiled.Count;i++) {
                    if (!compiled[i](item)) {
                        return false;
                    }
                }

                return true;
            };
        }

        private static Func<Torrent, bool>? Compile(FilteringDescriptor descriptor)
        {
            if (descriptor == null) {
                return null;
            }

            if (descriptor.Predicate != null) {
                var predicate = descriptor.Predicate;
                return item => predicate(item);
            }

            var selector = CreateSelector(descriptor);
            if (selector == null) {
                return null;
            }

            var culture = descriptor.Culture ?? CultureInfo.InvariantCulture;
            var stringComparison = descriptor.StringComparisonMode ?? StringComparison.OrdinalIgnoreCase;
            var values = descriptor.Values;
            var value = descriptor.Value;

            return descriptor.Operator switch {
                FilteringOperator.Equals => item => Equals(selector(item), value),
                FilteringOperator.NotEquals => item => !Equals(selector(item), value),
                FilteringOperator.Contains => item => Contains(selector(item), value, stringComparison),
                FilteringOperator.StartsWith => item => StartsWith(selector(item), value, stringComparison),
                FilteringOperator.EndsWith => item => EndsWith(selector(item), value, stringComparison),
                FilteringOperator.In => item => In(selector(item), values),
                _ => throw new NotImplementedException($"Unsupported filtering operator: {descriptor.Operator}")
            };
        }

        private static Func<Torrent, object?>? CreateSelector(FilteringDescriptor descriptor)
        {
            if (descriptor.ColumnId is DataGridColumnDefinition columnDefinition && columnDefinition.ValueAccessor != null) {
                return columnDefinition.ValueAccessor.GetValue;
            }

            return null;
        }

        private static bool Contains(object? source, object? target, StringComparison comparison)
        {
            if (source == null || target == null) {
                return false;
            }

            if (source is string s && target is string t) {
                return s.IndexOf(t, comparison) >= 0;
            }

            if (source is IEnumerable enumerable && source is not string) {
                foreach (var item in enumerable) {
                    if (Equals(item, target)) {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool StartsWith(object? source, object? target, StringComparison comparison)
        {
            if (source is string s && target is string t) {
                return s.StartsWith(t, comparison);
            }

            return false;
        }

        private static bool EndsWith(object? source, object? target, StringComparison comparison)
        {
            if (source is string s && target is string t) {
                return s.EndsWith(t, comparison);
            }

            return false;
        }

        private static bool In(object? source, IReadOnlyList<object?>? values)
        {
            if (values == null || values.Count == 0) {
                return false;
            }

            if (source is IEnumerable enumerable && source is not string) {
                foreach (var item in enumerable) {
                    foreach (var candidate in values) {
                        if (Equals(item, candidate)) {
                            return true;
                        }
                    }
                }

                return false;
            }

            foreach (var candidate in values) {
                if (Equals(source, candidate)) {
                    return true;
                }
            }

            return false;
        }

        private sealed class DynamicDataFilteringAdapter(IFilteringModel model, Func<IEnumerable<DataGridColumn>> columns, Action<IReadOnlyList<FilteringDescriptor>> FxUpdate) : DataGridFilteringAdapter(model, columns)
        {
            protected override bool TryApplyModelToView(
                IReadOnlyList<FilteringDescriptor> descriptors,
                IReadOnlyList<FilteringDescriptor> previousDescriptors,
                out bool changed)
            {
                FxUpdate(descriptors);
                changed = true;
                return true;
            }
        }

        private static bool AlwaysTrue(Torrent _) => true;
    }
}
