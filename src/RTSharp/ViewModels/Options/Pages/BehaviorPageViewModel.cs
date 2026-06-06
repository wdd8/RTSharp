using System;
using System.Collections.ObjectModel;
using System.Linq;

using Avalonia.Media.Imaging;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.DependencyInjection;

using RTSharp.Shared.Controls;

namespace RTSharp.ViewModels.Options.Pages;

public partial class PeerOriginReplacement : ObservableObject
{
    [ObservableProperty]
    public partial string Pattern { get; set; } = "";

    [ObservableProperty]
    public partial string Replacement { get; set; } = "";
}

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

    public Bitmap FlatQuestionMarkIcon { get; } = BuiltInIcon.Get(BuiltInIcons.FLAT_QUESTION_MARK);

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
