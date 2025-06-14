using System.Collections.ObjectModel;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls;
using Avalonia.Media;
using RTSharp.Models;
using RTSharp.Core;
using RTSharp.Shared.Abstractions;
using System.Linq;
using System.Threading.Tasks;
using Serilog;
using System;
using System.Reactive.Linq;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RTSharp.ViewModels.TorrentListing
{
    public partial class TorrentFilesViewModel : ObservableObject
    {
        public HierarchicalTreeDataGridSource<Models.File> Source { get; }

        public ObservableCollection<Models.File> Files = new();

        public bool MultiFile { get; set; }

        [ObservableProperty]
        public Models.Torrent torrent;

        public Action<string> ShowTextPreviewWindow { get; set; }

        public TorrentFilesViewModel()
        {
            Source = new HierarchicalTreeDataGridSource<Models.File>(Files) {
                Columns =
                {
                    new HierarchicalExpanderColumn<Models.File>(
                        new TextColumn<Models.File, string>("Name", x => x.Name),
                        x => x.Children,
                        hasChildrenSelector: x => x.IsDirectory,
                        isExpandedSelector: x => x.IsExpanded),
                    new TemplateColumn<Models.File>("Size", "SizeCell"),
                    new TemplateColumn<Models.File>("Downloaded", "DownloadedCell"),
                    new TemplateColumn<Models.File>("Done", "DoneCell"),
                    new TextColumn<Models.File, string>("Priority", x => x.Priority),
                    new TextColumn<Models.File, string>("Download strategy", x => x.DownloadStrategy),
                },
            };
            Source.RowSelection!.SingleSelect = false;
            Source.RowSelection.SelectionChanged += RowSelection_SelectionChanged;
        }

        private void RowSelection_SelectionChanged(object sender, Avalonia.Controls.Selection.TreeSelectionModelSelectionChangedEventArgs<Models.File> e)
        {
            for (var x = e.SelectedItems.Count - 1;x >= 0;x--) {
                if (IsVirtualRootItem(e.SelectedItems[x])) {
                    Source.RowSelection.Deselect(e.SelectedIndexes[x]);
                }
            }
        }

        private bool IsVirtualRootItem(Models.File file)
        {
            return file.Path == "./" || (MultiFile && file.Path == Torrent.Name);
        }

        public Geometry Icon { get; } = FontAwesomeIcons.Get("fa-solid fa-file");

        public bool CanExecuteMediaInfo() => Source.RowSelection.SelectedIndexes.Any();
        [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanExecuteMediaInfo))]
        public async Task MediaInfo(IReadOnlyList<object?> In)
        {
            var files = In.Cast<Models.File>().Where(x => x.Path != "./");

            if (files.Count() == 0)
                return;

            var server = Torrent.Owner.PluginInstance.AttachedDaemonService;

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
}
