using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Configuration;

using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Controls;

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace RTSharp.AutoIntegrify.Plugin.ViewModels;

public enum LinkOptions
{
    ReflinkWithHardlinkFallback,
    Reflink,
    Hardlink
}

public partial class SettingsWindowViewModel(IPluginHost PluginHost) : ObservableObject, IContextPopulatedNotifyable
{
    [ObservableProperty]
    public ObservableCollection<string> items;

    [ObservableProperty]
    public int randomPiecesToCheck;

    [ObservableProperty]
    public int searchRecursionLimit;

    [ObservableProperty]
    public string newPath;

    [ObservableProperty]
    public LinkOptions linkOptions;

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

    [RelayCommand]
    public async Task Save()
    {
        await PluginHost.SavePluginConfig(json => {
            json["SearchPaths"] = JsonSerializer.SerializeToNode(Items.ToList());
            json["RandomPiecesToCheck"] = JsonSerializer.SerializeToNode(RandomPiecesToCheck);
            json["SearchRecursionLimit"] = JsonSerializer.SerializeToNode(SearchRecursionLimit);
            json["LinkOptions"] = JsonSerializer.SerializeToNode(LinkOptions);
        });
    }

    public void OnContextPopulated()
    {
        var paths = PluginHost.PluginConfig.GetSection("SearchPaths").Get<List<string>>();
        paths ??= [];
        Items = new(paths);

        RandomPiecesToCheck = PluginHost.PluginConfig.GetValue("RandomPiecesToCheck", 5);
        SearchRecursionLimit = PluginHost.PluginConfig.GetValue("SearchRecursionLimit", 3);
        LinkOptions = PluginHost.PluginConfig.GetValue("LinkOptions", LinkOptions.ReflinkWithHardlinkFallback);
    }
}
