using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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
        public partial TorrentSelectionSummary? Summary { get; set; }

        [ObservableProperty]
        public partial IList<Shared.Abstractions.PieceState> Pieces { get; set; }

        public Func<string, Task> Copy = null!; // view set

        [RelayCommand]
        public async Task CopyInfoHash()
        {
            if (Torrent is null)
                return;

            await Copy(Convert.ToHexString(Torrent.Hash));
        }
    }
}
