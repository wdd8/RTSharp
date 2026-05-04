using Avalonia.Controls;
using Avalonia.Controls.DataGridConditionalFormatting;
using Avalonia.Controls.DataGridFiltering;
using Avalonia.Controls.DataGridSearching;
using Avalonia.Controls.DataGridSorting;
using Avalonia.Data.Converters;

using Microsoft.Extensions.DependencyInjection;

using RTSharp.Core.TorrentPolling;
using RTSharp.Models;
using RTSharp.Shared.Controls.DataGridFilters;
using RTSharp.Shared.Utils;

using Serilog;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace RTSharp.ViewModels.TorrentListing;

public partial class TorrentListingViewModel
{
    public class DataGridStatePersistenceTokenProvider : IDataGridStatePersistenceTokenProvider
    {
        public bool TryGetConditionalFormattingPredicateToken(ConditionalFormattingDescriptor descriptor, out string token)
        {
            Debugger.Break();
            token = null;
            return false;
        }
        public bool TryGetConditionalFormattingThemeToken(ConditionalFormattingDescriptor descriptor, out string token)
        {
            Debugger.Break();
            token = null;
            return false;
        }

        public bool TryGetFilteringPredicateToken(FilteringDescriptor descriptor, out string token)
        {
            var columnKey = (string)descriptor.ColumnId;
            if (columnKey == nameof(Torrent.TrackerDisplayName)) {
                token = "Filtering:TrackerDisplayName";
                return true;
            } else if (columnKey == nameof(Torrent.State)) {
                token = "Filtering:State";
                return true;
            } else if (columnKey == nameof(Torrent.Hash)) {
                token = "Filtering:Hash";
                return true;
            }

            Debugger.Break();
            token = null;
            return false;
        }

        public bool TryGetFilteringValueToken(FilteringDescriptor descriptor, object value, out string token)
        {
            var columnKey = (string)descriptor.ColumnId;
            if (value is TrackerFilterContext ctx) {
                token = "TrackerFilterContext:" + ctx.Name;
                return true;
            } else if (columnKey == "Connection") {
                token = "ConnectionFilterContext:" + (string)value;
                return true;
            } else if (columnKey == nameof(Torrent.Name)) {
                token = "NameFilterContext:" + (string)value;
                return true;
            } else if (columnKey == nameof(Torrent.Hash)) {
                token = "HashFilterContext:" + (string)value;
                return true;
            } else if (columnKey == nameof(Torrent.State)) {
                token = "StateFilterContext:" + (string)value;
                return true;
            } else if (columnKey == nameof(Torrent.Labels)) {
                token = "LabelsFilterContext:" + (string)value;
                return true;
            }

            Debugger.Break();
            token = null;
            return false;
        }

        public bool TryGetGroupingValueConverterToken(IValueConverter converter, out string token)
        {
            Debugger.Break();
            token = null;
            return false;
        }

        public bool TryGetSortingComparerToken(SortingDescriptor descriptor, out string token)
        {
            var columnKey = (string)descriptor.ColumnId;

            token = "Sorting:" + columnKey;
            return true;
        }
    }

    public class DataGridStatePersistenceTokenResolver : IDataGridStatePersistenceTokenResolver
    {
        public bool TryResolveConditionalFormattingPredicate(string token, out Func<ConditionalFormattingContext, bool> predicate)
        {
            Debugger.Break();
            predicate = null;
            return false;
        }
        public bool TryResolveConditionalFormattingTheme(string token, out Avalonia.Styling.ControlTheme theme)
        {
            Debugger.Break();
            theme = null;
            return false;
        }

        public bool TryResolveFilteringPredicate(string token, object value, List<object> values, out Func<object, bool> predicate)
        {
            if (token == "Filtering:TrackerDisplayName") {
                predicate = x => values.Any(i => ((TrackerFilterContext)i!).Name == ((Torrent)x!).TrackerDisplayName);
                return true;
            } else if (token == "Filtering:State") {
                predicate = x => values.Any(i => ((Torrent)x!).InternalState.HasFlag(Shared.Abstractions.EnumExt.ToTorrentState((string)i!)));
                return true;
            } else if (token == "Filtering:Hash") {
                predicate = x => {
                    if (value == null || !ConvertExtensions.TryFromHexString((string)value, out var res))
                        return false;
                    return res.SequenceEqual(((Torrent)x!).Hash);
                };
                return true;
            }

            Debugger.Break();
            predicate = null;
            return false;
        }

        public bool TryResolveFilteringValue(string token, out object value)
        {
            const string TRACKER_FILTER_CONTEXT = "TrackerFilterContext:";
            const string CONNECTION_FILTER_CONTEXT = "ConnectionFilterContext:";
            const string NAME_FILTER_CONTEXT = "NameFilterContext:";
            const string HASH_FILTER_CONTEXT = "HashFilterContext:";
            const string STATE_FILTER_CONTEXT = "StateFilterContext:";
            const string LABELS_FILTER_CONTEXT = "LabelsFilterContext:";

            if (token.StartsWith(TRACKER_FILTER_CONTEXT)) {
                value = new TrackerFilterContext {
                    Name = token[TRACKER_FILTER_CONTEXT.Length..],
                    Icon = null
                };
                return true;
            } else if (token.StartsWith(CONNECTION_FILTER_CONTEXT)) {
                value = token[CONNECTION_FILTER_CONTEXT.Length..];
                return true;
            } else if (token.StartsWith(NAME_FILTER_CONTEXT)) {
                value = token[NAME_FILTER_CONTEXT.Length..];
                return true;
            } else if (token.StartsWith(HASH_FILTER_CONTEXT)) {
                value = token[HASH_FILTER_CONTEXT.Length..];
                return true;
            } else if (token.StartsWith(STATE_FILTER_CONTEXT)) {
                value = token[STATE_FILTER_CONTEXT.Length..];
                return true;
            } else if (token.StartsWith(LABELS_FILTER_CONTEXT)) {
                value = token[LABELS_FILTER_CONTEXT.Length..];
                return true;
            }

            Debugger.Break();
            value = null;
            return false;
        }

        public bool TryResolveGroupingValueConverter(string token, out IValueConverter converter)
        {
            Debugger.Break();
            converter = null;
            return false;
        }

        public bool TryResolveSortingComparer(string token, out IComparer comparer)
        {
            comparer = null;
            return false;
        }
    }

    public void PostGridStateRestore()
    {
        foreach (var desc in FilteringModel.Descriptors) {
            if (desc.ColumnId is DataGridTemplateColumnDefinition template) {
                var columnKey = (string)template.ColumnKey;
                if (columnKey == nameof(Torrent.TrackerDisplayName)) {
                    foreach (var value in desc.Values) {
                        var ctx = (TrackerFilterContext)value;
                        var existing = TrackerFilter.Options.FirstOrDefault(x => x.Display.Name == ctx.Name);
                        if (existing != null) {
                            existing.IsSelected = true;
                        } else {
                            TrackerFilter.Options.Add(new SelectedOption<TrackerFilterContext> { Display = ctx, IsSelected = true });
                        }
                    }
                } else if (columnKey == nameof(Torrent.Hash)) {
                    HashFilter.Text = (string)desc.Value;
                }
            } else if (desc.ColumnId is DataGridTextColumnDefinition text) {
                var columnKey = (string)text.ColumnKey;
                if (columnKey == "Connection") {
                    RefillSelectedOptions(desc, ConnectionFilter);
                } else if (columnKey == nameof(Torrent.Name)) {
                    NameFilter.Text = (string)desc.Value;
                }  else if (columnKey == nameof(Torrent.State)) {
                    RefillSelectedOptions(desc, StateFilter);
                } else if (columnKey == nameof(Torrent.Labels)) {
                    RefillSelectedOptions(desc, LabelsFilter);
                }
            }
        }
    }

    private static void RefillSelectedOptions<T>(FilteringDescriptor desc, SetFilterContext<T> filterContext)
        where T : IComparable
    {
        foreach (var value in desc.Values) {
            var ctx = (T)value;
            var existing = filterContext.Options.FirstOrDefault(x => Object.Equals(x.Display, ctx));
            if (existing != null) {
                existing.IsSelected = true;
            } else {
                filterContext.Options.Add(new SelectedOption<T> { Display = ctx, IsSelected = true });
            }
        }
    }

    public void EvGridKeyUp(Avalonia.Input.KeyEventArgs e)
    {
        var now = DateTime.UtcNow;
        if (now - LastSearchKey > SearchAsYouGoDelay) {
            CurrentSearchText = "";
            SearchModel.Clear();
        }
        if (e.KeySymbol == null)
            return;

        CurrentSearchText += e.KeySymbol;
        if (CurrentSearchText.Length > 0) {
            SearchModel.SetOrUpdate(new SearchDescriptor(
                query: CurrentSearchText.Trim(),
                matchMode: SearchMatchMode.StartsWith,
                termMode: SearchTermCombineMode.All,
                scope: SearchScope.ExplicitColumns,
                columnIds: [
                    NameColumn,
                    LabelsColumn,
                    TrackerColumn
                ],
                comparison: StringComparison.OrdinalIgnoreCase,
                wholeWord: false
            ));
            Debug.WriteLine("Search: " + CurrentSearchText.Trim());

            if (SearchModel.CurrentResult != null)
                ScrollToItem(SearchModel.CurrentResult.Item);
            SearchModel.Clear();
        }

        LastSearchKey = now;
    }

    public void EvCopyingRowClipboardContent(DataGridRowClipboardEventArgs e)
    {
        if (e.IsColumnHeadersRow) {
            return;
        }

        for (var i = 0;i < e.ClipboardRowContent.Count;i++) {
            var cell = e.ClipboardRowContent[i];
            if ((string?)cell.Column?.Header == nameof(Torrent.Hash)) {
                e.ClipboardRowContent[i] = new DataGridClipboardCellContent(cell.Item, cell.Column, Convert.ToHexString(((Torrent)e.Item).Hash));
            } else if ((string?)cell.Column?.Header == nameof(Torrent.Done)) {
                var formatted = string.Format(CultureInfo.InvariantCulture, "{0:0.00} %", ((Torrent)e.Item).Done);
                e.ClipboardRowContent[i] = new DataGridClipboardCellContent(cell.Item, cell.Column, formatted);
            } else if ((string?)cell.Column?.Header == "Tracker") {
                e.ClipboardRowContent[i] = new DataGridClipboardCellContent(cell.Item, cell.Column, ((Torrent)e.Item).TrackerDisplayName ?? "");
            }
        }
    }

    public void StateFilterFlyoutOpening() => RepopulateOptions(StateFilter, Shared.Abstractions.EnumExt.GetAllTorrentStateOptions(), x => x);
    public void LabelsFilterFlyoutOpening() => RepopulateOptions(LabelsFilter, TorrentPolling.AllLabelReferences.Keys, x => x);
    public void ConnectionFilterFlyoutOpening() => RepopulateOptions(ConnectionFilter, TorrentPolling.Torrents.GetSnapshot(), x => x.DataOwner.PluginInstance.PluginInstanceConfig.Name);
    public void TrackerFilterFlyoutOpening() => RepopulateOptions(TrackerFilter, TorrentPolling.Torrents.GetSnapshot(), x => new TrackerFilterContext { Name = x.TrackerDisplayName!, Icon = x.TrackerIcon });
    public static void RepopulateOptions<TInput, TProp>(SetFilterContext<TProp> FilterContext, IEnumerable<TInput> Input, Func<TInput, TProp> FxProperty)
        where TProp : IComparable
    {
        var existingOptions = FilterContext.Options.ToDictionary(x => x.Display!, x => (Option: x, AccountedFor: false));

        foreach (var item in Input) {
            var key = FxProperty(item);

            if (existingOptions.TryGetValue(key, out var info) && !info.AccountedFor) {
                existingOptions[key] = info with { AccountedFor = true };
                FilterContext.Options.RemoveWhere(x => x.Display.Equals(key));
                FilterContext.Options.Add(new() { Display = key, IsSelected = info.Option.IsSelected });
            } else
                FilterContext.Options.Add(new() { Display = key, IsSelected = false });
        }

        foreach (var option in existingOptions) {
            if (!option.Value.AccountedFor) {
                FilterContext.Options.Remove(option.Value.Option);
            }
        }
    }

    private async ValueTask SaveGridState()
    {
        try {
            string? state = CaptureGridState();

            if (state != null) {
                using var scope = Core.ServiceProvider.CreateScope();
                var config = scope.ServiceProvider.GetRequiredService<Core.Config>();
                config.UIState.Value.TorrentGridState = state;
                await config.Rewrite();
            }
        } catch (Exception ex) {
            Log.Logger.Error(ex, "SaveGridState");
        }
    }
}
