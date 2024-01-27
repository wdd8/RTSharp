namespace RTSharp.Core.Services.Cache.TorrentFileCache
{
    public class CachedTorrentPath
    {
        public CachedTorrentPath()
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
