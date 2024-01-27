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
using Splat;

using Microsoft.Extensions.DependencyInjection;
using Splat.Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting.Internal;
using System.IO;
using MsBox.Avalonia;
using Tmds.DBus.Protocol;
using System.Collections.Generic;
using System.Net;
using RTSharp.Core.Services.Auxiliary;
using LiveChartsCore;
using System.Threading.Tasks;
using System.Threading;
using Avalonia.Threading;

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

            await RTSharp.Core.Services.Auxiliary.ConfigureServices.GenerateCertificatesIfNeeded();

            var host = Host.CreateDefaultBuilder()
				.ConfigureServices((_, services) => {
					services.UseMicrosoftDependencyResolver();
					var resolver = Locator.CurrentMutable;
					resolver.InitializeSplat();

                    services.AddSingleton<IConfiguration>(config);
                    Core.Config.AddConfig(config, services);

                    services.AddAuxiliaryServices(config.GetSection("Servers").Get<Dictionary<string, Server>>());

					services.AddTransient<Core.Services.Cache.TorrentFileCache.TorrentFileCache>();
					services.AddTransient<Core.Services.Cache.TorrentPropertiesCache.TorrentPropertiesCache>();
					services.AddTransient<Core.Services.Cache.ASCache.ASCache>();
					services.AddTransient<Core.Services.Cache.Images.ImageCache>();
					services.AddTransient<Core.Services.Cache.TrackerDb.TrackerDb>();
					services.AddHttpClient<Core.Services.Favicon>();
				})
				.Build();

			Core.ServiceProvider._provider = host.Services;
			Core.ServiceProvider._provider.UseMicrosoftDependencyResolver();
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
        }

        private static ConcurrentDictionary<string, Action> FxOnExit = new();

        public static void RegisterOnExit(string Key, Action Fx)
        {
			FxOnExit[Key] = Fx;
		}

        public static MainWindow MainWindow { get; private set; }

		public static MainWindowViewModel MainWindowViewModel { get; private set; }

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
                    MainWindowViewModel = new MainWindowViewModel();
                    MainWindow = new MainWindow(MainWindowViewModel);

                    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime) {
                        MainWindow.Closing += (_, _) =>
                        {
                            foreach (var (_, fx) in FxOnExit) {
                                fx();
                            }
                            MainWindowViewModel.CloseLayout();
                        };

                        desktopLifetime.MainWindow = MainWindow;

                        desktopLifetime.Exit += (_, _) =>
                        {
                            MainWindowViewModel.CloseLayout();
                        };
                    }

                    MainWindow.Show();
                });
            });

            base.OnFrameworkInitializationCompleted();
        }
    }
}