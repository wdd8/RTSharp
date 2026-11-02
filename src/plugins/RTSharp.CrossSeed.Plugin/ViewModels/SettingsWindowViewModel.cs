using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using RTSharp.Scripts.CrossSeedSearch;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.Client;

using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace RTSharp.CrossSeed.Plugin.ViewModels;

public partial class SettingsWindowViewModel(IPluginHost<CrossSeedPluginConfig> PluginHost) : ObservableViewModel
{
    [ObservableProperty]
    public partial ObservableCollection<CrossSeedIndexer> Indexers { get; set; } = [];

    [ObservableProperty]
    public partial CrossSeedIndexer? SelectedIndexer { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial string NewName { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial string NewUrl { get; set; } = "";

    [ObservableProperty]
    public partial LinkOptions LinkOptions { get; set; }

    [ObservableProperty]
    public partial int SearchDelaySeconds { get; set; }

    [RelayCommand]
    public void Remove(CrossSeedIndexer In) => Indexers.Remove(In);

    private bool CanAdd() => !String.IsNullOrWhiteSpace(NewName) && !String.IsNullOrWhiteSpace(NewUrl);

    [RelayCommand(CanExecute = nameof(CanAdd))]
    public void Add()
    {
        var indexer = new CrossSeedIndexer(NewName, NewUrl);
        var existing = Indexers.FirstOrDefault(x => x.Name.Equals(NewName, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
            Indexers[Indexers.IndexOf(existing)] = indexer;
        else
            Indexers.Add(indexer);

        SelectedIndexer = null;
        NewName = "";
        NewUrl = "";
    }

    partial void OnSelectedIndexerChanged(CrossSeedIndexer? value)
    {
        if (value == null)
            return;

        NewName = value.Name;
        NewUrl = value.Url;
    }

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(LinkOptions))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(CrossSeedIndexer))]
    [RelayCommand]
    public async Task Save()
    {
        await PluginHost.SavePluginConfig(settings => {
            settings.Indexers = [.. Indexers];
            settings.LinkOptions = LinkOptions;
            settings.SearchDelaySeconds = SearchDelaySeconds;
        });
    }

    public override void OnContextPopulated()
    {
        var config = PluginHost.Config;

        Indexers = new(config.Indexers);
        LinkOptions = config.LinkOptions;
        SearchDelaySeconds = config.SearchDelaySeconds;
    }
}
