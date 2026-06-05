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
//using ProDiagnostics.Transport;

using RTSharp.Core;
using RTSharp.Core.Services.Cache.ASCache;
using RTSharp.Core.Services.Cache.Images;
using RTSharp.Core.Services.Cache.TorrentFileCache;
using RTSharp.Core.Services.Cache.TorrentPropertiesCache;
using RTSharp.Core.Services.Daemon;
using RTSharp.Core.Services.Database.TrackerDb;
using RTSharp.Core.TorrentPolling;
using RTSharp.Shared.Controls;
using RTSharp.Shared.Controls.Views;
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

public static class Services
{
    public static async Task RegisterServices()
    {
        await Core.Config.WriteDefaultConfig();

        var cfgBuilder = new ConfigurationBuilder();
        cfgBuilder.AddJsonFile(Core.Config.ConfigPath, false, true);
        var config = cfgBuilder.Build();

        await ConfigureServices.GenerateCertificatesIfNeeded();

        var servers = config.GetSection("Servers").Get<Dictionary<string, Config.Models.Server>>() ?? [];

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) => {
                services.UseMicrosoftDependencyResolver();

                services.AddSingleton<IConfiguration>(config);
                Core.Config.AddConfig(config, services);

                services.AddDaemonServices(servers);

                services.AddTransient<Core.Services.Cache.TorrentFileCache.TorrentFileCache>();
                services.AddTransient<Core.Services.Cache.TorrentPropertiesCache.TorrentPropertiesCache>();
                services.AddTransient<Core.Services.Cache.ASCache.ASCache>();
                services.AddTransient<Core.Services.Cache.Images.ImageCache>();
                services.AddTransient<TrackerDb>();
                services.AddSingleton<Shared.Abstractions.ISpeedMovingAverageService, Core.Services.SpeedMovingAverageService>();
                services.AddSingleton<Core.Services.DomainParser>();
                services.AddSingleton<Core.Services.MediaPreviewService>();
                services.AddHttpClient<Core.Services.Favicon>();
            })
            .Build();

        Core.ServiceProvider._provider = host.Services;
        Core.ServiceProvider._provider.UseMicrosoftDependencyResolver();

        foreach (var server in servers) {
            var instance = ActivatorUtilities.CreateInstance<DaemonService>(Core.ServiceProvider._provider, server.Key);
            Core.Servers.Value.Add(server.Key, instance);
            Dispatcher.UIThread.Invoke(() => {
                var renderer = new ServersActionQueueRenderer(server.Key, instance);
                ActionQueue.RegisterActionQueue(instance, renderer);
                _ = renderer.TrackServerActions();
            });
        }
    }
}

public class App : Application
{
    [STAThread]
    static void Main(string[] args)
    {
        //AppContext.SetSwitch("ProDataGrid.Diagnostics.IsEnabled", true);

        var log = new LoggerConfiguration()
            .MinimumLevel.Verbose()
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
        AvaloniaXamlLoader.Load(this);

#if DEBUG
        this.AttachDevTools();
#endif

        /*var diagnosticsExporter = new DiagnosticsUdpExporter(new DiagnosticsUdpOptions {
            ActivitySourceNames = new[] { "ProDataGrid.Diagnostic.Source", "Avalonia.Diagnostic.Source" },
            MeterNames = new[] { "ProDataGrid.Diagnostic.Meter", "Avalonia.Diagnostic.Meter" }
        });

        diagnosticsExporter.Start();*/
    }

    private static ConcurrentDictionary<string, Func<ValueTask>> FxOnExit = new();

    public static void RegisterOnExit(string Key, Func<ValueTask> Fx)
    {
        FxOnExit[Key] = Fx;
    }

    public static MainWindow MainWindow { get; private set; } = null!;

    public static MainWindowViewModel MainWindowViewModel { get; private set; } = null!;

    public override void OnFrameworkInitializationCompleted()
    {
        var tcs = new TaskCompletionSource();
        var thread = new Thread(() => {
            Services.RegisterServices().GetAwaiter().GetResult();
            tcs.SetResult();
        });
        thread.Start();

        tcs.Task.ContinueWith((task) => {
            Dispatcher.UIThread.Invoke(async () => {
                try {
                    this.DataContext = new AppViewModel();
                    MainWindowViewModel = new MainWindowViewModel();
                    MainWindow = new MainWindow(MainWindowViewModel);

                    using var scope = Core.ServiceProvider.CreateScope();
                    var tasks = Task.WhenAll(
                        Task.Run(scope.ServiceProvider.GetRequiredService<TorrentFileCache>().Initialize),
                        Task.Run(scope.ServiceProvider.GetRequiredService<TorrentPropertiesCache>().Initialize),
                        Task.Run(scope.ServiceProvider.GetRequiredService<ASCache>().Initialize),
                        Task.Run(scope.ServiceProvider.GetRequiredService<ImageCache>().Initialize),
                        Task.Run(scope.ServiceProvider.GetRequiredService<TrackerDb>().Initialize),
                        Task.Run(() => Plugin.Plugins.LoadPlugins((progress, text) => { })),
                        Task.Run(scope.ServiceProvider.GetRequiredService<Core.Services.DomainParser>().Initialize)
                    );

                    TorrentPolling.Start();
                    await tasks;

                    Log.Logger.Information("Ready");

                    MainWindowViewModel!.PostStartup();
                } catch (Exception ex) {
                    var msgbox = MessageBoxManager.GetMessageBoxStandard(
                        title: "RTSharp has crashed", 
                        text: $"RTSharp has crashed.\n{ex}", 
                        @enum: MsBox.Avalonia.Enums.ButtonEnum.Ok, 
                        icon: MsBox.Avalonia.Enums.Icon.Error, 
                        windowStartupLocation: WindowStartupLocation.CenterOwner);
                    var task = msgbox.ShowAsync();
                    var cts = new CancellationTokenSource();
                    _ = task.ContinueWith((task) => cts.Cancel());
                    Dispatcher.UIThread.MainLoop(cts.Token);
#if !DEBUG
                    Environment.Exit(1);
#endif
                    throw;
                }

                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime) {
                    desktopLifetime.MainWindow = MainWindow;

                    bool firstExit = true;

                    desktopLifetime.ShutdownRequested += (sender, e) => {
                        if (firstExit) {
                            e.Cancel = true;

                            var tcs = new TaskCompletionSource();
                            var thread = new Thread(() => {
                                foreach (var (_, fx) in FxOnExit) {
                                    fx().GetAwaiter().GetResult();
                                }

                                firstExit = false;
                                Dispatcher.UIThread.Post(() => {
                                    desktopLifetime.Shutdown();
                                });
                            });
                            thread.Start();
                        }
                    };
                }

                MainWindow.Show();
            });
        });

        base.OnFrameworkInitializationCompleted();
    }

    public void Exit(object sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime) {
            desktopLifetime.Shutdown();
        }
    }
}