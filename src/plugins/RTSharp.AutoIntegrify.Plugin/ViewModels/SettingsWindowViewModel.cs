using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using RTSharp.Shared.Abstractions.Client;

using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace RTSharp.AutoIntegrify.Plugin.ViewModels;

public enum LinkOptions
{
    ReflinkWithHardlinkFallback,
    Reflink,
    Hardlink
}

public partial class SettingsWindowViewModel(IPluginHost<AutoIntegrifyPluginConfig> PluginHost) : ObservableViewModel
{
    [ObservableProperty]
    public partial ObservableCollection<string> Items { get; set; }

    [ObservableProperty]
    public partial int RandomPiecesToCheck { get; set; }

    [ObservableProperty]
    public partial int SearchRecursionLimit { get; set; }

    [ObservableProperty]
    public partial string NewPath { get; set; }

    [ObservableProperty]
    public partial LinkOptions LinkOptions { get; set; }

    [RelayCommand]
    public async Task Remove(string In)
    {
        Items.Remove(In);
    }
    
    [RelayCommand]
    public async Task Add()
    {
        Items.Add(NewPath);
    }

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(LinkOptions))]
    [RelayCommand]
    public async Task Save()
    {
        await PluginHost.SavePluginConfig(settings => {
            settings.SearchPaths = [.. Items];
            settings.RandomPiecesToCheck = RandomPiecesToCheck;
            settings.SearchRecursionLimit = SearchRecursionLimit;
            settings.LinkOptions = LinkOptions;
        });
    }

    public override void OnContextPopulated()
    {
        Items = new(PluginHost.Config.SearchPaths);

        RandomPiecesToCheck = PluginHost.Config.RandomPiecesToCheck;
        SearchRecursionLimit = PluginHost.Config.SearchRecursionLimit;
        LinkOptions = PluginHost.Config.LinkOptions;
    }
}
