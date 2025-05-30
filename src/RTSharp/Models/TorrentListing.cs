using System;
using System.Collections.ObjectModel;

namespace RTSharp.Models;

public class TorrentListing
{
    public ObservableCollection<Torrent> Torrents { get; set; }
    
    public TorrentListing()
    {
        Torrents = new ObservableCollection<Torrent>() {
            new Torrent(new[] { (byte)0 }, null) {
                Name = "Name1",
                Size = 4516458712,
                Done = 99.5884824565415f,
                Peers = new ConnectedTotalPair(2, 5),
                CreatedOnDate = DateTime.Now
            },
            new Torrent(new[] { (byte)0 }, null) {
                Name = "Name2",
                Size = 4516458712,
                Done = 54.5f,
                AddedOnDate = DateTime.Now
            },
            new Torrent(new[] { (byte)0 }, null) {
                Name = "Name3",
                Size = 4516458712,
                Done = 54.5f,
                AddedOnDate = DateTime.Now,
                FinishedOnDate = DateTime.UtcNow,
                TrackerSingle = "http://www.com/",
                Priority = "Nice"
            },
            new Torrent(new[] { (byte)0 }, null) {
                Name = "Name4",
                Size = 4516458712,
                Done = 54.5f,
                AddedOnDate = DateTime.Now,
                Ratio = 1.21354f
            }
        };
    }
}