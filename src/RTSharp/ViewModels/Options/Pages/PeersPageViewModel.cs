using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

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

public partial class PeersPageViewModel : ObservableObject, ISettingsLoadable
{
    public ObservableCollection<PeerOriginReplacement> PeerOriginReplacements { get; } = [];

    [ObservableProperty]
    public partial PeerOriginReplacement? SelectedPeerOriginReplacement { get; set; }

    public Func<string, (string Caption, string Text)[], Task<string[]?>> PromptText { get; set; } = null!; // view set

    [ObservableProperty]
    public partial string PeerOriginLookupMethodHelpText { get; set; } = "";

    public ObservableCollection<WhoisProviderItem> WhoisProviders { get; } = [];

    [ObservableProperty]
    public partial WhoisProviderItem? SelectedWhoisProvider { get; set; }

    public PeersPageViewModel()
    {
        if (!Avalonia.Controls.Design.IsDesignMode)
            Load();
    }

    public void Load()
    {
        using var scope = Core.ServiceProvider.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<Core.Config>();
        var behavior = config.Behavior.Value;

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
        if (SelectedWhoisProvider != null)
            behavior.PeerOriginLookupMethod = SelectedWhoisProvider.Id;

        behavior.PeerOriginReplacements.Clear();
        foreach (var item in PeerOriginReplacements.Where(x => !String.IsNullOrEmpty(x.Pattern)))
            behavior.PeerOriginReplacements[item.Pattern] = item.Replacement;
    }

    [RelayCommand]
    public async Task AddReplacement()
    {
        var input = await PromptText("RT# - Add Peer Origin Replacement", [("Patt_ern:", ""), ("Repla_cement:", "")]);
        if (input is not [var pattern, var replacement] || String.IsNullOrEmpty(pattern) || String.IsNullOrEmpty(replacement))
            return;

        PeerOriginReplacements.Add(new PeerOriginReplacement {
            Pattern = pattern,
            Replacement = replacement
        });
    }

    [RelayCommand]
    public void RemoveReplacement(PeerOriginReplacement item) => PeerOriginReplacements.Remove(item);
}
