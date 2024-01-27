namespace RTSharp.Shared.Abstractions
{
    public record DataProviderFilesCapabilities(
        bool GetDotTorrents,
        bool GetDefaultSavePath
    );
}
