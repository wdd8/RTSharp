using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.Extensions.DependencyInjection;

using System;
using System.Collections.Generic;

namespace RTSharp.ViewModels.Options.Pages;

public partial class LookPageViewModel : ObservableObject, ISettingsLoadable
{
    [ObservableProperty]
    public partial bool SpeedChartEnabled { get; set; }

    [ObservableProperty]
    public partial int RowHeight { get; set; }

    [ObservableProperty]
    public partial bool StateIcons { get; set; }

    [ObservableProperty]
    public partial bool AlternatingRowColors { get; set; }

    public IReadOnlyList<string> Themes { get; } = Core.Themes.List();

    [ObservableProperty]
    public partial string Theme { get; set; } = Core.Themes.Standard;

    public IReadOnlyList<Core.Config.Models.Look.Variant> ThemeVariants { get; } = Enum.GetValues<Core.Config.Models.Look.Variant>();

    [ObservableProperty]
    public partial Core.Config.Models.Look.Variant ThemeVariant { get; set; }

    public LookPageViewModel()
    {
        if (!Avalonia.Controls.Design.IsDesignMode)
            Load();
    }

    public void Load()
    {
        using var scope = Core.ServiceProvider.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<Core.Config>();
        var look = config.Look.Value;

        SpeedChartEnabled = look.DataProviders.SpeedChartEnabled;
        RowHeight = look.TorrentListing.RowHeight;
        StateIcons = look.TorrentListing.StateIcons;
        AlternatingRowColors = look.AlternatingRowColors;
        Theme = look.Theme;
        ThemeVariant = look.ThemeVariant;
    }

    public void ApplyToConfig(Core.Config config)
    {
        var look = config.Look.Value;
        look.DataProviders.SpeedChartEnabled = SpeedChartEnabled;
        look.TorrentListing.RowHeight = RowHeight;
        look.TorrentListing.StateIcons = StateIcons;
        look.AlternatingRowColors = AlternatingRowColors;
        look.ThemeVariant = ThemeVariant;

        if (Theme != null)
            look.Theme = Theme;
    }
}
