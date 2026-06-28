using RTSharp.Daemon.Protocols.DataProvider;
using RTSharp.Shared.Utils;

namespace RTSharp.Daemon.Services.rtorrent.TorrentPoll
{
    public readonly struct HistoryEntry
    {
        public DateTime Date { get; }
        public InfoHashDictionary<Torrent> List { get; }
        public InfoHashDictionary<TorrentsTrackersReply.Types.TorrentsTrackers> Trackers { get; }

        public HistoryEntry(DateTime date, InfoHashDictionary<Torrent> list, InfoHashDictionary<TorrentsTrackersReply.Types.TorrentsTrackers> trackers)
        {
            Date = date;
            List = list;
            Trackers = trackers;
        }
    }
}
