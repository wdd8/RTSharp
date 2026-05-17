using Avalonia.Controls;
using Avalonia.Controls.DataGridHierarchical;
using Avalonia.Controls.DataGridSelection;
using Avalonia.Data.Core;
using Avalonia.Markup.Xaml.MarkupExtensions.CompiledBindings;
using Avalonia.Media;
using Avalonia.Styling;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DynamicData;

using RTSharp.Core;
using RTSharp.Models;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Controls.DataGridFilters;

using Serilog;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace RTSharp.ViewModels.TorrentListing;

internal static class ColumnDefinitionBindingFactory
{
    public static DataGridBindingDefinition CreateBinding<TItem, TValue>(
        string name,
        Func<TItem, TValue> getter,
        Action<TItem, TValue>? setter = null)
    {
        if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException("Property name is required.", nameof(name));
        }

        if (getter == null) {
            throw new ArgumentNullException(nameof(getter));
        }

        var propertyInfo = new ClrPropertyInfo(
            name,
            target => TryGetValue(target, getter),
            setter == null
                ? null
                : (target, value) => TrySetValue(target, value, setter),
            typeof(TValue));

        return DataGridBindingDefinition.Create<TItem, TValue>(propertyInfo, getter, setter);
    }

    private static TValue TryGetValue<TItem, TValue>(object target, Func<TItem, TValue> getter)
    {
        if (target is not TItem item) {
            return default!;
        }

        return getter(item);
    }

    private static void TrySetValue<TItem, TValue>(object target, object? value, Action<TItem, TValue> setter)
    {
        if (target is not TItem item) {
            return;
        }

        if (value is null) {
            setter(item, default!);
            return;
        }

        if (value is TValue typedValue) {
            setter(item, typedValue);
            return;
        }

        setter(item, (TValue)value);
    }
}

public partial class TorrentFilesViewModel : ObservableObject
{
    public Models.File? File { get; private set; }

    public ObservableCollection<DataGridColumnDefinition> ColumnDefinitions { get; } = new();
    public HierarchicalModel<Models.File> Model { get; }

    public bool MultiFile { get; set; }

    [ObservableProperty]
    public partial Models.Torrent? Torrent { get; set; }
    public Action<string> ShowTextPreviewWindow { get; set; }

    private static DataGridBindingDefinition CreateNodeBinding<TValue>(string name, Func<Models.File, TValue> getter)
    {
        return ColumnDefinitionBindingFactory.CreateBinding<HierarchicalNode, TValue>(
            name,
            node => getter((Models.File)node.Item));
    }

    public void ClearRoot()
    {
        File = null;
        Model.SetRoots([]);
    }

    public void SetRoot(Models.File Root)
    {
        File = Root;
        Model.SetRoot(File);
    }

    public TorrentFilesViewModel()
    {
        var options = new HierarchicalOptions<Models.File> {
            ChildrenSelector = x => x.Children,
            IsLeafSelector = x => !x.IsDirectory,
            AutoExpandRoot = true,
            MaxAutoExpandDepth = 2,
            VirtualizeChildren = true
        };

        Model = new HierarchicalModel<Models.File>(options);

        ColumnDefinitions.AddRange([
            new DataGridHierarchicalColumnDefinition {
                Header = "Name",
                Binding = CreateNodeBinding<Models.File>("Item", item => item),
                CellTemplateKey = "FileNameTemplate",
                ColumnKey = nameof(Models.File.Name),
                SortMemberPath = nameof(Models.File.Name)
            },
            new DataGridTemplateColumnDefinition
            {
                Header = nameof(Models.File.Size),
                ValueAccessor = new DataGridColumnValueAccessor<Models.File, Models.File>(x => x),
                CellTemplateKey = "FileSizeTemplate",
                ColumnKey = nameof(Models.File.Size),
                SortMemberPath = nameof(Models.File.Size),
            },
            new DataGridTemplateColumnDefinition
            {
                Header = nameof(Models.File.Downloaded),
                CellTemplateKey = "FileDownloadedTemplate",
                ColumnKey = nameof(Models.File.Downloaded),
                SortMemberPath = nameof(Models.File.Downloaded),
            },
            new DataGridTemplateColumnDefinition
            {
                Header = nameof(Models.File.Done),
                CellTemplateKey = "FileDoneTemplate",
                ColumnKey = nameof(Models.File.Done),
                SortMemberPath = nameof(Models.File.Done)
            },
            new DataGridTextColumnDefinition
            {
                Header = "Priority",
                Binding = CreateNodeBinding("Priority", item => item.Priority),
                ColumnKey = nameof(Models.File.Priority),
            },
            new DataGridTextColumnDefinition
            {
                Header = "Download strategy",
                Binding = CreateNodeBinding("DownloadStrategy", item => item.DownloadStrategy),
                ColumnKey = nameof(Models.File.DownloadStrategy)
            }
        ]);

    }

    public Geometry Icon { get; } = FontAwesomeIcons.Get("fa7-solid fa7-file");

    public bool CanExecuteMediaInfo() => true;
    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanExecuteMediaInfo))]
    public async Task MediaInfo(SelectedItemsView In)
    {
        var files = In.Cast<Models.File>().Where(x => x.Path != "./");

        if (files.Count() == 0 || Torrent == null)
            return;

        var server = Torrent.DataOwner.PluginInstance.AttachedDaemonService;

        IList<string> reply;
        try {
            reply = await server.Mediainfo(files.Select(x => x.GetRemotePath(Torrent.RemotePath, Torrent.Name, MultiFile)).ToArray());
        } catch (Exception ex) {
            Log.Logger.Error(ex, "Mediainfo failed");
            return;
        }

        ShowTextPreviewWindow(string.Join('\n', reply));
    }
}
