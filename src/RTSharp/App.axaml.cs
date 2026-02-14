using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using RTSharp.ViewModels;
using RTSharp.Views;

using System;
using Avalonia.Logging;
using Projektanker.Icons.Avalonia;
using Projektanker.Icons.Avalonia.FontAwesome;
using RTSharp.Core;
using Serilog;
using Serilog.Configuration;

using Microsoft.Extensions.DependencyInjection;
using Splat.Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using MsBox.Avalonia;
using System.Collections.Generic;
using RTSharp.Core.Services.Daemon;
using System.Threading.Tasks;
using System.Threading;
using Avalonia.Threading;
using NP.Ava.UniDock;

namespace RTSharp
{
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

            var servers = config.GetSection("Servers").Get<Dictionary<string, Config.Models.Server>>();

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
                    services.AddTransient<Core.Services.Cache.TrackerDb.TrackerDb>();
                    services.AddSingleton<Shared.Abstractions.ISpeedMovingAverageService, Core.Services.SpeedMovingAverageService>();
                    services.AddSingleton<Core.Services.DomainParser>();
                    services.AddHttpClient<Core.Services.Favicon>();
                })
                .Build();

            Core.ServiceProvider._provider = host.Services;
            Core.ServiceProvider._provider.UseMicrosoftDependencyResolver();

            foreach (var server in servers) {
                var instance = ActivatorUtilities.CreateInstance<DaemonService>(Core.ServiceProvider._provider, server.Key);
                Core.Servers.Value.Add(server.Key, instance);
                var renderer = new ServersActionQueueRenderer(server.Key);
                ActionQueue.RegisterActionQueue(instance, renderer);
                _ = renderer.TrackServerActions(instance);
            }
        }
    }

    public class App : Application
    {
        [STAThread]
        static void Main(string[] args)
        {
            var log = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Debug()
                .WriteTo.LogWindow()
                .CreateLogger();
            Log.Logger = log;

            IconProvider.Current.Register<FontAwesomeIconProvider>();

            try {
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnMainWindowClose);
            } catch (Exception ex) {
                var msgbox = MessageBoxManager.GetMessageBoxStandard("RTSharp has crashed", $"RTSharp has crashed.\n{ex}", MsBox.Avalonia.Enums.ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error, WindowStartupLocation.CenterOwner);
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
                         .LogToTrace(LogEventLevel.Information);

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);

#if DEBUG
            this.AttachDeveloperTools();
#endif
        }

        private static ConcurrentDictionary<string, Func<ValueTask>> FxOnExit = new();

        public static void RegisterOnExit(string Key, Func<ValueTask> Fx)
        {
            FxOnExit[Key] = Fx;
        }

        public static MainWindow MainWindow { get; private set; }

        public static MainWindowViewModel MainWindowViewModel { get; private set; }

        public static DockManager DockManager { get; internal set; }

        public override void OnFrameworkInitializationCompleted()
        {
            var tcs = new TaskCompletionSource();
            var thread = new Thread(() => {
                Services.RegisterServices().GetAwaiter().GetResult();
                tcs.SetResult();
            });
            thread.Start();

            tcs.Task.ContinueWith((task) => {
                Dispatcher.UIThread.Invoke(() => {
                    try {
                        this.DataContext = new AppViewModel();
                        MainWindowViewModel = new MainWindowViewModel();
                        MainWindow = new MainWindow(MainWindowViewModel);
                    } catch (Exception ex) {
                        var msgbox = MessageBoxManager.GetMessageBoxStandard("RTSharp has crashed", $"RTSharp has crashed.\n{ex}", MsBox.Avalonia.Enums.ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error, WindowStartupLocation.CenterOwner);
                        var task = msgbox.ShowAsync();
                        var cts = new CancellationTokenSource();
                        task.ContinueWith((task) => cts.Cancel());
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
                            }

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
}