using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.DependencyInjection;

using RTSharp.Shared.Abstractions.Client;

namespace RTSharp.ViewModels.Options.Pages;

public partial class PeerOriginReplacement : ObservableObject
{
    [ObservableProperty]
    public partial string Pattern { get; set; } = "";

    [ObservableProperty]
    public partial string Replacement { get; set; } = "";
}

public record WhoisProviderItem(string Id, string Name);

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

    public ObservableCollection<PeerOriginReplacement> PeerOriginReplacements { get; } = [];

    [ObservableProperty]
    public partial PeerOriginReplacement? SelectedPeerOriginReplacement { get; set; }

    [ObservableProperty]
    public partial string NewReplacementPattern { get; set; } = "";

    [ObservableProperty]
    public partial string NewReplacementReplacement { get; set; } = "";

    [ObservableProperty]
    public partial bool UseFaviconGoogleAPI { get; set; }

    [ObservableProperty]
    public partial bool PollActiveTabOnly { get; set; }

    [ObservableProperty]
    public partial bool AutoStartRecheckedTorrents { get; set; }

    [ObservableProperty]
    public partial string PeerOriginLookupMethodHelpText { get; set; } = "";

    public ObservableCollection<WhoisProviderItem> WhoisProviders { get; } = [];

    [ObservableProperty]
    public partial WhoisProviderItem? SelectedWhoisProvider { get; set; }

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

        var whoisProviders = scope.ServiceProvider.GetRequiredService<IEnumerable<IWhoisProvider>>();
        WhoisProviders.Clear();
        PeerOriginLookupMethodHelpText = "";
        foreach (var provider in whoisProviders) {
            WhoisProviders.Add(new WhoisProviderItem(provider.Id, provider.DisplayName));
            PeerOriginLookupMethodHelpText += $"* {provider.DisplayName}: {provider.HelpText}\n\n";
        }
        PeerOriginLookupMethodHelpText = PeerOriginLookupMethodHelpText.TrimEnd();

        SelectedWhoisProvider = WhoisProviders.FirstOrDefault(x => x.Id == behavior.PeerOriginLookupMethod) ?? WhoisProviders.FirstOrDefault();

        PeerOriginReplacements.Clear();
        foreach (var (pattern, replacement) in behavior.PeerOriginReplacements)
            PeerOriginReplacements.Add(new PeerOriginReplacement { Pattern = pattern, Replacement = replacement });
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
        if (SelectedWhoisProvider != null)
            behavior.PeerOriginLookupMethod = SelectedWhoisProvider.Id;

        behavior.PeerOriginReplacements.Clear();
        foreach (var item in PeerOriginReplacements.Where(x => !String.IsNullOrEmpty(x.Pattern)))
            behavior.PeerOriginReplacements[item.Pattern] = item.Replacement;
    }

    [RelayCommand]
    public void AddReplacement()
    {
        if (String.IsNullOrEmpty(NewReplacementPattern) || String.IsNullOrEmpty(NewReplacementReplacement))
            return;

        PeerOriginReplacements.Add(new PeerOriginReplacement {
            Pattern = NewReplacementPattern,
            Replacement = NewReplacementReplacement
        });
        NewReplacementPattern = "";
        NewReplacementReplacement = "";
        DialogHostAvalonia.DialogHost.Close("AddReplacementDialog");
    }

    [RelayCommand]
    public void RemoveReplacement(PeerOriginReplacement item) => PeerOriginReplacements.Remove(item);
}
