namespace RTSharp.Core.Services.Cache.TorrentPropertiesCache
{
    public class CachedTorrentProperties
    {
        public CachedTorrentProperties()
        {
        }

        public CachedTorrentProperties(bool IsMultiFile)
        {
            this.IsMultiFile = IsMultiFile;
        }

        public bool IsMultiFile { get; init; }

        public void Deconstruct(out bool IsMultiFile)
        {
            IsMultiFile = this.IsMultiFile;
        }
    }
}
