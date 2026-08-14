using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Logging;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using Dapper;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using MsBox.Avalonia;

using Optris.Icons.Avalonia;
using Optris.Icons.Avalonia.FontAwesome7;

using RTSharp.Core;
using RTSharp.Core.Services.Cache.ASCache;
using RTSharp.Core.Services.Cache.Images;
using RTSharp.Core.Services.Cache.TorrentFileCache;
using RTSharp.Core.Services.Cache.TorrentPropertiesCache;
using RTSharp.Core.Services.Daemon;
using RTSharp.Core.Services.Database.TrackerDb;
using RTSharp.Core.TorrentPolling;
using RTSharp.ViewModels;
using RTSharp.Views;

using Serilog;
using Serilog.Configuration;

using Splat.Microsoft.Extensions.DependencyInjection;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

[module: DapperAot]

namespace RTSharp;

public static class SerilogSinkExtensions
{
    public static LoggerConfiguration LogWindow(
              this LoggerSinkConfiguration loggerConfiguration,
              IFormatProvider? formatProvider = null)
    {
        return loggerConfiguration.Sink(new LogWindowSink(formatProvider));
    }
}

public class App : Application
{
    [STAThread]
    static void Main(string[] args)
    {
        var log = new LoggerConfiguration()
#if DEBUG
            .MinimumLevel.Verbose()
#else
            .MinimumLevel.Information()
#endif
            .WriteTo.Debug()
            .WriteTo.LogWindow()
            .CreateLogger();
        Log.Logger = log;

        IconProvider.Current.Register<FontAwesome7IconProvider>();

        try {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnMainWindowClose);
        } catch (Exception ex) {
            var msgbox = MessageBoxManager.GetMessageBoxStandard(
                title: "RTSharp has crashed", 
                text: $"RTSharp has crashed.\n{ex}", 
                @enum: MsBox.Avalonia.Enums.ButtonEnum.Ok, 
                icon: MsBox.Avalonia.Enums.Icon.Error, 
                windowStartupLocation: WindowStartupLocation.CenterOwner);
            var task = msgbox.ShowAsync();
            var cts = new CancellationTokenSource();
            task.ContinueWith((task) => cts.Cancel());
            Dispatcher.UIThread.MainLoop(cts.Token);
#if !DEBUG
            Environment.Exit(1);
#endif
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
                     .UsePlatformDetect()
                     .LogToTrace(LogEventLevel.Warning);

    public override void Initialize()
    {
        _ = Task.Run(async () => {
            try {
                await PreInitServices();
                PreInitServiceTcs.SetResult();
            } catch (Exception ex) {
                await Dispatcher.UIThread.InvokeAsync(async () => {
                    var msgbox = MessageBoxManager.GetMessageBoxStandard(
                        title: "RTSharp has crashed",
                        text: $"RTSharp has crashed.\n{ex}",
                        @enum: MsBox.Avalonia.Enums.ButtonEnum.Ok,
                        icon: MsBox.Avalonia.Enums.Icon.Error,
                        windowStartupLocation: WindowStartupLocation.CenterOwner);
                    await msgbox.ShowAsync();
                });
                Environment.Exit(1);
                return;
            }
        });

        AvaloniaXamlLoader.Load(this);

#if DEBUG
        this.AttachDeveloperTools();
#endif

        /*var diagnosticsExporter = new DiagnosticsUdpExporter(new DiagnosticsUdpOptions {
            ActivitySourceNames = new[] { "ProDataGrid.Diagnostic.Source", "Avalonia.Diagnostic.Source" },
            MeterNames = new[] { "ProDataGrid.Diagnostic.Meter", "Avalonia.Diagnostic.Meter" }
        });

        diagnosticsExporter.Start();*/
    }

    private static ConcurrentDictionary<string, Func<ValueTask>> FxOnExit = new();

    private static TaskCompletionSource PreInitServiceTcs = new();

    public static void RegisterOnExit(string Key, Func<ValueTask> Fx)
    {
        FxOnExit[Key] = Fx;
    }

    public static MainWindow MainWindow { get; private set; } = null!;

    public static MainWindowViewModel MainWindowViewModel { get; private set; } = null!;

    public override void OnFrameworkInitializationCompleted()
    {
        _ = Task.Run(async () => {
            try {
                await PreInitServiceTcs.Task;
                await PostInitServices();
            } catch (Exception ex) {
                await Dispatcher.UIThread.InvokeAsync(async () => {
                    var msgbox = MessageBoxManager.GetMessageBoxStandard(
                        title: "RTSharp has crashed",
                        text: $"RTSharp has crashed.\n{ex}",
                        @enum: MsBox.Avalonia.Enums.ButtonEnum.Ok,
                        icon: MsBox.Avalonia.Enums.Icon.Error,
                        windowStartupLocation: WindowStartupLocation.CenterOwner);
                    await msgbox.ShowAsync();
                });
                Environment.Exit(1);
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(InitUI);
        });

        base.OnFrameworkInitializationCompleted();
    }

    public static async Task PreInitServices()
    {
        await Core.Config.WriteDefaultConfig();

        var cfgBuilder = new ConfigurationBuilder();
        cfgBuilder.AddJsonFile(RTSharp.Shared.Abstractions.Consts.APP_CONFIG_PATH, false, true);
        var config = cfgBuilder.Build();

        Core.GlobalProxy.Install(config.GetSection(nameof(Config.Models.Proxy)).Get<Config.Models.Proxy>());

        await ConfigureServices.GenerateCertificatesIfNeeded();

#pragma warning disable IL2026 // Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code
        await Plugin.Plugins.LoadPlugins((progress, text) => { });
#pragma warning restore IL2026 // Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) => {
                services.UseMicrosoftDependencyResolver();

                services.AddSingleton<IConfiguration>(config);
                Core.Config.AddConfig(config, services);

                services.AddTransient<Core.Services.Cache.TorrentFileCache.TorrentFileCache>();
                services.AddTransient<Core.Services.Cache.TorrentPropertiesCache.TorrentPropertiesCache>();
                services.AddTransient<Core.Services.Cache.ASCache.ASCache>();
                services.AddTransient<Core.Services.Cache.Images.ImageCache>();
                services.AddTransient<TrackerDb>();
                services.AddSingleton<Shared.Abstractions.ISpeedMovingAverageService, Core.Services.SpeedMovingAverageService>();
                services.AddSingleton<Core.Services.DomainParser>();
                services.AddSingleton<Shared.Abstractions.Client.IWhoisProvider, Core.Services.Whois.RdapWhoisProvider>();
                services.AddSingleton<Shared.Abstractions.Client.IWhoisProvider, Core.Services.Whois.CaidaTeamCymruWhoisProvider>();
                services.AddSingleton<Core.Services.MediaPreviewService>();
                services.AddHttpClient<Core.Services.Favicon>()
                    .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler {
                        Proxy = Core.GlobalProxy.Instance,
                        UseProxy = true
                    });

#pragma warning disable IL2026 // Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code
                Plugin.Plugins.ConfigureServices(services);
#pragma warning restore IL2026 // Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code
            })
            .Build();

        Core.ServiceProvider._provider = host.Services;
        Core.ServiceProvider._provider.UseMicrosoftDependencyResolver();
    }

    public static async Task PostInitServices()
    {
        using var scope = Core.ServiceProvider.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        var servers = config.GetSection("Servers").Get<Dictionary<string, Config.Models.Server>>() ?? [];

        foreach (var server in servers)
            Core.Servers.Add(server.Key, server.Value);

        var tasks = Task.WhenAll(
            Task.Run(scope.ServiceProvider.GetRequiredService<TorrentFileCache>().Initialize),
            Task.Run(scope.ServiceProvider.GetRequiredService<TorrentPropertiesCache>().Initialize),
            Task.Run(scope.ServiceProvider.GetRequiredService<ASCache>().Initialize),
            Task.Run(scope.ServiceProvider.GetRequiredService<ImageCache>().Initialize),
            Task.Run(scope.ServiceProvider.GetRequiredService<TrackerDb>().Initialize),
            Task.Run(scope.ServiceProvider.GetRequiredService<Core.Services.DomainParser>().Initialize)
        );

        await tasks;
    }

    private async Task InitUI()
    {
        try {
            this.DataContext = new AppViewModel();
            MainWindowViewModel = new MainWindowViewModel();
            MainWindow = new MainWindow(MainWindowViewModel);

#pragma warning disable IL2026 // Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code
            await Task.Run(() => Plugin.Plugins.InitPlugins((progress, text) => { }));
#pragma warning restore IL2026

            TorrentPolling.Start();

            MainWindowViewModel!.PostStartup();

            _ = Core.InstalledScriptsSync.CheckOrphans();
        } catch (Exception ex) {
            var msgbox = MessageBoxManager.GetMessageBoxStandard(
                title: "RTSharp has crashed",
                text: $"RTSharp has crashed.\n{ex}",
                @enum: MsBox.Avalonia.Enums.ButtonEnum.Ok,
                icon: MsBox.Avalonia.Enums.Icon.Error,
                windowStartupLocation: WindowStartupLocation.CenterOwner);
            await msgbox.ShowAsync();
            Environment.Exit(1);
            return;
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime) {
            desktopLifetime.MainWindow = MainWindow;

            bool firstExit = true;

            desktopLifetime.ShutdownRequested += async (sender, e) => {
                if (firstExit) {
                    e.Cancel = true;
                    firstExit = false;
                    await Task.Run(async () => {
                        foreach (var (_, fx) in FxOnExit)
                            await fx();
                    });
                    desktopLifetime.Shutdown();
                }
            };
        }

        MainWindow.Show();
    }

    public void Exit(object sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime) {
            desktopLifetime.Shutdown();
        }
    }
}