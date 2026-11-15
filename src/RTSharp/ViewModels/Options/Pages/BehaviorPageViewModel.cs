using System;

using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.Extensions.DependencyInjection;

namespace RTSharp.ViewModels.Options.Pages;

public partial class BehaviorPageViewModel : ObservableObject, ISettingsLoadable
{
    [ObservableProperty]
    public partial double FilesPollingIntervalSeconds { get; set; }

    [ObservableProperty]
    public partial double PeersPollingIntervalSeconds { get; set; }

    [ObservableProperty]
    public partial double TrackersPollingIntervalSeconds { get; set; }

    [ObservableProperty]
    public partial double SearchAsYouGoDelaySeconds { get; set; }

    [ObservableProperty]
    public partial bool UseFaviconGoogleAPI { get; set; }

    [ObservableProperty]
    public partial bool PollActiveTabOnly { get; set; }

    [ObservableProperty]
    public partial bool AutoStartRecheckedTorrents { get; set; }

    public BehaviorPageViewModel()
    {
        if (!Avalonia.Controls.Design.IsDesignMode)
            Load();
    }

    public void Load()
    {
        using var scope = Core.ServiceProvider.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<Core.Config>();
        var behavior = config.Behavior.Value;

        FilesPollingIntervalSeconds = behavior.FilesPollingInterval.TotalSeconds;
        PeersPollingIntervalSeconds = behavior.PeersPollingInterval.TotalSeconds;
        TrackersPollingIntervalSeconds = behavior.TrackersPollingInterval.TotalSeconds;
        SearchAsYouGoDelaySeconds = behavior.SearchAsYouGoDelay.TotalSeconds;
        UseFaviconGoogleAPI = behavior.UseFaviconGoogleAPI;
        PollActiveTabOnly = behavior.PollActiveTabOnly;
        AutoStartRecheckedTorrents = behavior.AutoStartRecheckedTorrents;
    }

    public void ApplyToConfig(Core.Config config)
    {
        var behavior = config.Behavior.Value;
        behavior.FilesPollingInterval = TimeSpan.FromSeconds(FilesPollingIntervalSeconds);
        behavior.PeersPollingInterval = TimeSpan.FromSeconds(PeersPollingIntervalSeconds);
        behavior.TrackersPollingInterval = TimeSpan.FromSeconds(TrackersPollingIntervalSeconds);
        behavior.SearchAsYouGoDelay = TimeSpan.FromSeconds(SearchAsYouGoDelaySeconds);
        behavior.UseFaviconGoogleAPI = UseFaviconGoogleAPI;
        behavior.PollActiveTabOnly = PollActiveTabOnly;
        behavior.AutoStartRecheckedTorrents = AutoStartRecheckedTorrents;
    }
}
