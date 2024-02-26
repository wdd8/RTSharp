using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using RTSharp.Core;
using RTSharp.Models;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RTSharp.ViewModels.TorrentListing
{
    public partial class GeneralTorrentInfoViewModel : ObservableObject
    {
        [ObservableProperty]
        public Torrent? torrent;

        [ObservableProperty]
        public IList<Shared.Abstractions.PieceState> pieces;

        public Geometry Icon { get; } = FontAwesomeIcons.Get("fa-solid fa-circle-info");

        public Func<string, Task> Copy;

        [RelayCommand]
        public async Task CopyInfoHash()
        {
            await Copy(Convert.ToHexString(Torrent!.Hash));
        }
    }
}
