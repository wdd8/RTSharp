using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.DependencyInjection;

using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

using RTSharp.ViewModels.Options.Pages;

namespace RTSharp.ViewModels.Options;

public record OptionsItem(string Icon, string Path, string Text, IList<OptionsItem> Children);

public partial class OptionsWindowViewModel : ObservableObject
{
    public List<OptionsItem> Items { get; }

    public Dictionary<string, ISettingsLoadable> Pages { get; } = [];

    [ObservableProperty]
    public partial OptionsItem CurrentlySelectedItem { get; set; }

    [ObservableProperty]
    public partial object? SettingsContent { get; set; }

    public Action FxClose { get; set; } = null!; // view set

    public OptionsWindowViewModel()
    {
        Items = [
            new OptionsItem("fa7-solid fa7-gears", "Behavior", "Behavior", Array.Empty<OptionsItem>()),
            new OptionsItem("fa7-solid fa7-book-bookmark", "Caching", "Caching", Array.Empty<OptionsItem>()),
            new OptionsItem("fa7-solid fa7-palette", "Look", "Look", Array.Empty<OptionsItem>()),
        ];

        Pages = new Dictionary<string, ISettingsLoadable> {
            { "Behavior", new BehaviorPageViewModel() },
            { "Caching", new CachingPageViewModel() },
            { "Look", new LookPageViewModel() },
        };

        CurrentlySelectedItem = Items[0];
    }

    partial void OnCurrentlySelectedItemChanged(OptionsItem value)
    {
        SettingsContent = GetPage(value.Path);
    }

    public object? GetPage(string path)
    {
        if (!Pages.TryGetValue(path, out var vm))
            return null;

        return vm;
    }

    [RelayCommand]
    public async Task Apply()
    {
        using var scope = Core.ServiceProvider.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<Core.Config>();

        foreach (var (_, vm) in Pages) {
            vm.ApplyToConfig(config);
        }

        await config.Rewrite();
        await MessageBoxManager.GetMessageBoxStandard("RT# - Options", "Some changes may require a restart of RTSharp to take effect.", ButtonEnum.Ok, Icon.Warning).ShowAsync();
    }

    [RelayCommand]
    public async Task Ok()
    {
        await Apply();
        FxClose.Invoke();
    }

    [RelayCommand]
    public void Cancel() => FxClose.Invoke();
}
