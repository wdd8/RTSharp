using System;

namespace RTSharp.Core.Services.Cache.TorrentFileCache
{
    public class CachedTorrentPath
    {
        [Obsolete("For serialization only", true)]
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
        public CachedTorrentPath()
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
        {
        }

        public CachedTorrentPath(int OrderId, string Path, ulong Size)
        {
            this.OrderId = OrderId;
            this.Path = Path;
            this.Size = Size;
        }

        public int OrderId { get; init; }
        public string Path { get; init; }
        public ulong Size { get; init; }

        public void Deconstruct(out int OrderId, out string Path, out ulong Size)
        {
            OrderId = this.OrderId;
            Path = this.Path;
            Size = this.Size;
        }
    }
}
