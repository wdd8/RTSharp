using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.Extensions.DependencyInjection;

namespace RTSharp.ViewModels.Options.Pages;

public partial class CachingPageViewModel : ObservableObject, ISettingsLoadable
{
    [ObservableProperty]
    public partial bool FilesCachingEnabled { get; set; }

    [ObservableProperty]
    public partial int ConcurrentPeerCachingRequests { get; set; }

    [ObservableProperty]
    public partial int InMemoryImages { get; set; }

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
    }

    public void ApplyToConfig(Core.Config config)
    {
        var caching = config.Caching.Value;
        caching.FilesCachingEnabled = FilesCachingEnabled;
        caching.ConcurrentPeerCachingRequests = ConcurrentPeerCachingRequests;
        caching.InMemoryImages = InMemoryImages;
    }
}
