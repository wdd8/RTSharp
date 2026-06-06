using System.Threading.Tasks;

using Avalonia.Media.Imaging;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.DependencyInjection;

using RTSharp.Shared.Controls;

namespace RTSharp.ViewModels.Options.Pages;

public partial class CachingPageViewModel : ObservableObject, ISettingsLoadable
{
    [ObservableProperty]
    public partial bool FilesCachingEnabled { get; set; }

    [ObservableProperty]
    public partial int ConcurrentPeerCachingRequests { get; set; }

    [ObservableProperty]
    public partial int InMemoryImages { get; set; }

    public Bitmap FlatQuestionMarkIcon { get; } = BuiltInIcon.Get(BuiltInIcons.FLAT_QUESTION_MARK);

    [ObservableProperty]
    public partial string ASCacheInfo { get; set; } = "Loading...";

    [ObservableProperty]
    public partial string TorrentFileCacheInfo { get; set; } = "Loading...";

    [ObservableProperty]
    public partial string TorrentPropertiesCacheInfo { get; set; } = "Loading...";

    [ObservableProperty]
    public partial string ImageCacheInfo { get; set; } = "Loading...";

    public CachingPageViewModel()
    {
        if (!Avalonia.Controls.Design.IsDesignMode)
            Load();
    }

    public void Load()
    {
        using var scope = Core.ServiceProvider.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<Core.Config>();
        var caching = config.Caching.Value;

        FilesCachingEnabled = caching.FilesCachingEnabled;
        ConcurrentPeerCachingRequests = caching.ConcurrentPeerCachingRequests;
        InMemoryImages = caching.InMemoryImages;

        _ = LoadCacheStatsAsync();
    }

    private async Task LoadCacheStatsAsync()
    {
        using var scope = Core.ServiceProvider.CreateScope();
        var asCache = scope.ServiceProvider.GetRequiredService<Core.Services.Cache.ASCache.ASCache>();
        var torrentFileCache = scope.ServiceProvider.GetRequiredService<Core.Services.Cache.TorrentFileCache.TorrentFileCache>();
        var torrentPropertiesCache = scope.ServiceProvider.GetRequiredService<Core.Services.Cache.TorrentPropertiesCache.TorrentPropertiesCache>();
        var imageCache = scope.ServiceProvider.GetRequiredService<Core.Services.Cache.Images.ImageCache>();

        try {
            var (count, size) = await asCache.GetStats();
            ASCacheInfo = $"{count} entries, {Shared.Utils.Converters.GetSIDataSize((ulong)size)}";
        } catch {
            ASCacheInfo = "N/A";
        }

        try {
            var (count, size) = await torrentFileCache.GetStats();
            TorrentFileCacheInfo = $"{count} torrents, {Shared.Utils.Converters.GetSIDataSize((ulong)size)}";
        } catch {
            TorrentFileCacheInfo = "N/A";
        }

        try {
            var (count, size) = await torrentPropertiesCache.GetStats();
            TorrentPropertiesCacheInfo = $"{count} entries, {Shared.Utils.Converters.GetSIDataSize((ulong)size)}";
        } catch {
            TorrentPropertiesCacheInfo = "N/A";
        }

        try {
            var (count, size) = await imageCache.GetStats();
            ImageCacheInfo = $"{count} images, {Shared.Utils.Converters.GetSIDataSize((ulong)size)}";
        } catch {
            ImageCacheInfo = "N/A";
        }
    }

    public void ApplyToConfig(Core.Config config)
    {
        var caching = config.Caching.Value;
        caching.FilesCachingEnabled = FilesCachingEnabled;
        caching.ConcurrentPeerCachingRequests = ConcurrentPeerCachingRequests;
        caching.InMemoryImages = InMemoryImages;
    }

    [RelayCommand]
    public async Task ClearASCache()
    {
        using var scope = Core.ServiceProvider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<Core.Services.Cache.ASCache.ASCache>().Clear();
        _ = LoadCacheStatsAsync();
    }

    [RelayCommand]
    public async Task ClearTorrentFileCache()
    {
        using var scope = Core.ServiceProvider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<Core.Services.Cache.TorrentFileCache.TorrentFileCache>().Clear();
        _ = LoadCacheStatsAsync();
    }

    [RelayCommand]
    public async Task ClearTorrentPropertiesCache()
    {
        using var scope = Core.ServiceProvider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<Core.Services.Cache.TorrentPropertiesCache.TorrentPropertiesCache>().Clear();
        _ = LoadCacheStatsAsync();
    }

    [RelayCommand]
    public async Task ClearImageCache()
    {
        using var scope = Core.ServiceProvider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<Core.Services.Cache.Images.ImageCache>().Clear();
        await scope.ServiceProvider.GetRequiredService<Core.Services.Database.TrackerDb.TrackerDb>().ClearImageHashes();
        _ = LoadCacheStatsAsync();
    }
}
