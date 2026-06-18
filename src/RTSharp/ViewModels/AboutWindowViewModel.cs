using Avalonia.Platform;

using CommunityToolkit.Mvvm.ComponentModel;

using RTSharp.Shared.Abstractions.Client;

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace RTSharp.ViewModels;

public partial class AboutWindowViewModel : ObservableViewModel
{
    public IReadOnlyList<string> Libraries { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RTSharpPage))]
    public partial string? SelectedLibrary { get; set; }

    [ObservableProperty]
    public partial string? LicenseText { get; set; }

    public bool RTSharpPage => SelectedLibrary == null;

    public string Version { get; }

    public Action UnrestrictPageHeight { get; set; }

    public AboutWindowViewModel()
    {
        Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "";

        Libraries = [
            "Arin.NET",
            "AvaloniaUI",
            "Avalonia.AvaloniaEdit",
            "Ben.StringIntern",
            "BencodeNET",
            "CommunityToolkit.Mvvm",
            "ConcurrentHashSet",
            "Dapper",
            "DialogHost.Avalonia",
            "Dock.Avalonia",
            "DynamicData",
            "FluentMigrator",
            "Google.Protobuf",
            "grpc-dotnet",
            "HtmlAgilityPack",
            "ini-parser-netstandard",
            "IPAddressRange",
            "LiveChartsCore.SkiaSharpView.Avalonia",
            "Markdown.Avalonia",
            "MessageBox.Avalonia",
            "Nager.PublicSuffix",
            "Nito.AsyncEx",
            "Nito.Disposables",
            "Optris.Icons.Avalonia.FontAwesome7",
            "Polly",
            "ProDataGrid",
            "QBittorrent.Client",
            "Serilog",
            "Serilog.Sinks.Debug",
            "SkiaSharp",
            "Splat.Microsoft.Extensions.DependencyInjection",
            "Svg.Skia",
            "System.Linq.Async",
            "System.Reactive",
            "Xaml.Behaviors.Interactions",
            "Xdg.Directories",
            ".NET",
        ];

        SelectedLibrary = null;
    }

    partial void OnSelectedLibraryChanged(string? value)
    {
        if (value == null) {
            LicenseText = null;
            return;
        } else {
            UnrestrictPageHeight();
        }

        using var stream = AssetLoader.Open(new Uri($"avares://RTSharp/Assets/Licenses/{value}.txt"));
        using var reader = new StreamReader(stream);
        LicenseText = reader.ReadToEnd();
    }
}
