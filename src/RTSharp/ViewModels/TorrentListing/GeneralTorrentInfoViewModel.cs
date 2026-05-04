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
        public partial Torrent? Torrent { get; set; }

        [ObservableProperty]
        public partial IList<Shared.Abstractions.PieceState> Pieces { get; set; }
        public Geometry Icon { get; } = FontAwesomeIcons.Get("fa7-solid fa7-circle-info");

        public Func<string, Task> Copy;

        [RelayCommand]
        public async Task CopyInfoHash()
        {
            if (Torrent is null)
                return;

            await Copy(Convert.ToHexString(Torrent.Hash));
        }
    }
}
