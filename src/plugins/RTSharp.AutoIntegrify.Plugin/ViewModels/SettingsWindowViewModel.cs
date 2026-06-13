using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Configuration;

using RTSharp.Shared.Abstractions.Client;

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
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
        await PluginHost.SavePluginConfig(json => {
#pragma warning disable IL2026 // Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code
            json["SearchPaths"] = JsonSerializer.SerializeToNode(Items.ToList());
            json["RandomPiecesToCheck"] = JsonSerializer.SerializeToNode(RandomPiecesToCheck);
            json["SearchRecursionLimit"] = JsonSerializer.SerializeToNode(SearchRecursionLimit);
            json["LinkOptions"] = JsonSerializer.SerializeToNode(LinkOptions);
#pragma warning restore IL2026 // Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code
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
