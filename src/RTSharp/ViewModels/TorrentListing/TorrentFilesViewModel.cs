using System.Collections.ObjectModel;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls;
using Avalonia.Media;
using RTSharp.Models;
using RTSharp.Core;
using RTSharp.Shared.Abstractions;
using System.Linq;
using System.Windows.Input;
using System.Threading.Tasks;
using Serilog;
using System;
using System.Reactive;
using System.Reactive.Linq;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RTSharp.Core.Services.Daemon;
using NP.Ava.UniDockService;

namespace RTSharp.ViewModels.TorrentListing
{
    public partial class TorrentFilesViewModel : ObservableObject
    {
        public HierarchicalTreeDataGridSource<Models.File> Source { get; }

        public ObservableCollection<Models.File> Files = new();

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
        }

        public Geometry Icon { get; } = FontAwesomeIcons.Get("fa-solid fa-file");

        [RelayCommand]
        public async Task MediaInfo(IReadOnlyList<object?> In)
        {
            var files = In.Cast<Models.File>();

            var server = Torrent.Owner.PluginInstance.AttachedDaemonService;

            IList<string> reply;
            try {
                reply = await server.Mediainfo(files.Select(x => x.Path).ToArray());
            } catch (Exception ex) {
                Log.Logger.Error(ex, "Mediainfo failed");
                return;
            }

            ShowTextPreviewWindow(string.Join('\n', reply));
        }
    }
}
