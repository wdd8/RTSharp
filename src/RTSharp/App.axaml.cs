using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Logging;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Rendering.Composition;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

using Dapper;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using MsBox.Avalonia;
using MsBox.Avalonia.Base;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Models;

using RTSharp.Core;
using RTSharp.Core.Services.Cache.ASCache;
using RTSharp.Core.Services.Cache.Images;
using RTSharp.Core.Services.Cache.TorrentFileCache;
using RTSharp.Core.Services.Cache.TorrentPropertiesCache;
using RTSharp.Core.Services.Daemon;
using RTSharp.Core.Services.Database.TrackerDb;
using RTSharp.Core.TorrentPolling;
using RTSharp.Shared.Controls;
using RTSharp.ViewModels;
using RTSharp.Views;
using RTSharp.Views.TorrentListing;

using Serilog;
using Serilog.Configuration;

using Splat.Microsoft.Extensions.DependencyInjection;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
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
        try {
            GC.TryStartNoGCRegion(240 * 1024 * 1024);
        } catch (ArgumentOutOfRangeException) { }

        if (OperatingSystem.IsWindows())
            _ = Task.Run(WarmUpDirect3D);

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

        /*
         * Startup work runs from here on, while Avalonia sets up the platform. It logs, and the log window sink posts to
         * the UI thread, which is whichever thread uses Dispatcher.UIThread first
         */
        _ = Dispatcher.UIThread;

        var configLoad = Task.Run(() => {
            Core.Config.WriteDefaultConfig();
            return new ConfigurationBuilder()
                .AddJsonFile(RTSharp.Shared.Abstractions.Consts.APP_CONFIG_PATH, false, true)
                .Build();
        });

        ThemePrepare = Task.Run(() => PrepareTheme(configLoad));
        DockStateParse = Task.Run(async () => {
            var dockState = (await configLoad).GetSection(nameof(Config.Models.UIState))[nameof(Config.Models.UIState.DockState)];
            return String.IsNullOrEmpty(dockState) ? null : DockStateSerializer.Parse(dockState);
        });
        TorrentGridStateParse = Task.Run(async () => TorrentListingView.ParseGridState((await configLoad).GetSection(nameof(Config.Models.UIState))[nameof(Config.Models.UIState.TorrentGridState)]));

        var databases = Task.Run(() => MigrateDatabases(configLoad));
        ServiceProviderBuild = Task.Run(() => PreInitServices(configLoad));
        var servicesInit = Task.Run(() => PostInitServices(ServiceProviderBuild, databases));
        HooksInit = Task.Run(() => InitHooks(ServiceProviderBuild));
        DataProvidersInit = Task.Run(() => InitDataProviders(servicesInit, HooksInit));

        try {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnMainWindowClose);
        } catch (Exception ex) {
            // The lifetime's loop has ended, so the dialog gets one of its own
            var cts = new CancellationTokenSource();
            CrashMsgBox(ex).ShowAsync().ContinueWith(_ => cts.Cancel());
            Dispatcher.UIThread.MainLoop(cts.Token);
#if !DEBUG
            Environment.Exit(1);
#endif
            throw;
        }
    }

    [SupportedOSPlatform("windows")]
    private static void WarmUpDirect3D()
    {
        int[] featureLevels = [0xb100, 0xb000, 0xa100, 0xa000, 0x9300, 0x9200, 0x9100];

        // D3D_DRIVER_TYPE_HARDWARE, D3D11_SDK_VERSION
        if (D3D11CreateDevice(0, 1, 0, 0, featureLevels, (uint)featureLevels.Length, 7, out var device, out _, out var context) < 0)
            return;

        Marshal.Release(context);
        Marshal.Release(device);

        [DllImport("d3d11.dll")]
        static extern int D3D11CreateDevice(nint Adapter, int DriverType, nint Software, uint Flags, int[] FeatureLevels, uint FeatureLevelCount, uint SdkVersion, out nint Device, out int FeatureLevel, out nint ImmediateContext);
    }

    private static IMsBox<string> CrashMsgBox(Exception ex)
    {
        Bitmap? icon = null;
        try {
            icon = BuiltInIcon.Get(BuiltInIcons.CRASH);
        } catch (Exception iconEx) {
            Log.Logger.Warning(iconEx, "Could not load crash icon");
        }

        return MessageBoxManager.GetMessageBoxCustom(new MessageBoxCustomParams {
            ButtonDefinitions = [ new ButtonDefinition { Name = "OK", IsDefault = true } ],
            ImageIcon = icon!,
            Icon = icon == null ? MsBox.Avalonia.Enums.Icon.Error : MsBox.Avalonia.Enums.Icon.None,
            ContentTitle = "RTSharp has crashed",
            ContentMessage = $"RTSharp has crashed.\n{ex}",
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        });
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
                     .UsePlatformDetect()
                     .LogToTrace(LogEventLevel.Warning);

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        try {
            var (themeName, themeAssembly) = ThemePrepare.GetAwaiter().GetResult();

            IStyle theme;

            try {
                theme = Themes.Load(themeAssembly ?? Themes.LoadAssembly(themeName));
            } catch (Exception ex) when (themeName != Themes.Standard) {
                Log.Logger.Error(ex, $"Theme {themeName} failed to load");
                ThemeLoadError = ex.Message;
                theme = Themes.Load(Themes.LoadAssembly(Themes.Standard));
            }

            Styles.Insert(0, theme);
        } catch {
            // Lets the crash dialog render
            Styles.Insert(0, new FluentTheme());
            throw;
        }

        ThemeLoaded.SetResult();

#if DEBUG
        this.AttachDeveloperTools();
#endif

        /*var diagnosticsExporter = new DiagnosticsUdpExporter(new DiagnosticsUdpOptions {
            ActivitySourceNames = new[] { "ProDataGrid.Diagnostic.Source", "Avalonia.Diagnostic.Source" },
            MeterNames = new[] { "ProDataGrid.Diagnostic.Meter", "Avalonia.Diagnostic.Meter" }
        });

        diagnosticsExporter.Start();*/
    }

    private static string? ThemeLoadError;

    private static ConcurrentDictionary<string, Func<ValueTask>> FxOnExit = new();

    private static Task<(string Name, System.Reflection.Assembly? Assembly)> ThemePrepare = null!;

    private static Task<DockNodeDto?> DockStateParse = null!;

    private static Task<DataGridPersistedState?> TorrentGridStateParse = null!;

    private static Task ServiceProviderBuild = null!;

    private static Task HooksInit = null!;

    private static Task DataProvidersInit = null!;

    // Continuations run off the UI thread, which sets it
    private static readonly TaskCompletionSource ThemeLoaded = new(TaskCreationOptions.RunContinuationsAsynchronously);

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
                await ServiceProviderBuild;
            } catch (Exception ex) {
                await Dispatcher.UIThread.InvokeAsync(() => CrashMsgBox(ex).ShowAsync());
                Environment.Exit(1);
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(InitUI);
        });

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task<(string Name, System.Reflection.Assembly? Assembly)> PrepareTheme(Task<IConfigurationRoot> ConfigLoad)
    {
        var config = await ConfigLoad;

        Themes.CreateMissingBuiltInThemes();

        string themeName;
        try {
            themeName = config.GetSection(nameof(Config.Models.Look)).Get<Config.Models.Look>()?.Theme ?? Themes.Standard;
        } catch (Exception ex) {
            Log.Logger.Warning(ex, "Could not read theme from config");
            themeName = Themes.Standard;
        }

        try {
            return (themeName, Themes.LoadAssembly(themeName));
        } catch (Exception ex) {
            Log.Logger.Debug(ex, $"Theme {themeName} failed to prepare");
            return (themeName, null);
        }
    }

    private static async Task MigrateDatabases(Task<IConfigurationRoot> ConfigLoad)
    {
        await ConfigLoad;

        await Task.WhenAll(
            Task.Run(TorrentFileCache.Migrate),
            Task.Run(TorrentPropertiesCache.Migrate),
            Task.Run(ASCache.Migrate),
            Task.Run(ImageCache.Migrate),
            Task.Run(TrackerDb.Migrate)
        );
    }

    public static async Task PreInitServices(Task<IConfigurationRoot> ConfigLoad)
    {
        var config = await ConfigLoad;

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
                services.AddSingleton<TrackerDb>();
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

    public static async Task PostInitServices(Task ServiceProviderBuild, Task Databases)
    {
        await ServiceProviderBuild;

        using var scope = Core.ServiceProvider.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        var servers = config.GetSection("Servers").Get<Dictionary<string, Config.Models.Server>>() ?? [];

        var daemons = await Task.WhenAll(servers.Select(x => Task.Run(() => Core.Servers.Connect(x.Key, x.Value))));

        var tasks = Task.WhenAll(
            Task.Run(async () => {
                await Databases;
                await scope.ServiceProvider.GetRequiredService<TrackerDb>().Initialize();
            }),
            Task.Run(scope.ServiceProvider.GetRequiredService<Core.Services.DomainParser>().Initialize)
        );

        // Servers' action queues show the server icon, which comes from the theme
        await ThemeLoaded.Task;
        foreach (var daemon in daemons)
            Core.Servers.AddActionQueue(daemon);

        await tasks;
    }

    private static async Task InitHooks(Task ServiceProviderBuild)
    {
        // Plugins are loaded, and have configured their services
        await ServiceProviderBuild;

        // A plugin failing to initialize shows a message box, which takes the theme
        await ThemeLoaded.Task;

        await Plugin.Plugins.InitHooks();
    }

    private static async Task InitDataProviders(Task ServicesInit, Task HooksInit)
    {
        await ServicesInit;
        await HooksInit;

        TorrentPolling.Start();

        await Plugin.Plugins.InitDataProviders();
    }

    private async Task InitUI()
    {
        try {
            using (var scope = Core.ServiceProvider.CreateScope()) {
                var lookMonitor = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<Config.Models.Look>>();
                lookMonitor.OnChange(look => Dispatcher.UIThread.Post(() => SetAlternatingRowColors(look.AlternatingRowColors)));
                SetAlternatingRowColors(lookMonitor.CurrentValue.AlternatingRowColors);
            }
            // Not :nth-child, the grid moves rows to other indexes without raising IChildIndexProvider.ChildIndexChanged, e.g. when refreshing hierarchical rows
            DataGridRow.IndexProperty.Changed.AddClassHandler<DataGridRow>((row, _) => row.Classes.Set("odd", row.Index % 2 == 1));

            this.DataContext = new AppViewModel();
            MainWindowViewModel = new MainWindowViewModel(await TorrentGridStateParse);
            MainWindow = new MainWindow(MainWindowViewModel, await DockStateParse);

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

            // Plugins add to the main window's menus in Init, so it runs once the window exists
            var pluginsInit = Task.Run(async () => {
                await DataProvidersInit;
                await Plugin.Plugins.InitPlugins((progress, text) => { });
            });

            MainWindow.Show();

            /*
             * The window isn't drawn until its first frame renders, and the layout pass that builds the dock layout and the
             * torrent listing in it would hold that frame back. It runs once the render thread has the frame, which it
             * renders without the UI thread
             */
            if (ElementComposition.GetElementVisual(MainWindow)?.Compositor is { } compositor)
                await compositor.RequestCompositionBatchCommitAsync().Processed;

            // Plugins hook loading torrent rows, so the dock layout, and the torrents of the listing in it, come after
            await HooksInit;
            MainWindowViewModel!.PostStartup();

            // Runs after the layout pass that builds the listing and its rows
            Dispatcher.UIThread.Post(() => {
                if (System.Runtime.GCSettings.LatencyMode == System.Runtime.GCLatencyMode.NoGCRegion)
                    GC.EndNoGCRegion();
            }, DispatcherPriority.Background);

            await pluginsInit;

            _ = Core.InstalledScriptsSync.CheckOrphans();
        } catch (Exception ex) {
            await CrashMsgBox(ex).ShowAsync();
            Environment.Exit(1);
            return;
        }

        if (ThemeLoadError != null) {
            await MessageBoxManager.GetMessageBoxStandard(
                title: "RT# - Theme",
                text: $"{ThemeLoadError}\n\nThe {Themes.Standard} theme is used instead.",
                @enum: MsBox.Avalonia.Enums.ButtonEnum.Ok,
                icon: MsBox.Avalonia.Enums.Icon.Warning).ShowAsync();
        }

        void SetAlternatingRowColors(bool Enabled)
        {
            // Application resources are looked up before the theme's, so a null entry hides the theme's brush
            if (Enabled)
                Resources.Remove("DataGridAlternatingRowBrush");
            else
                Resources["DataGridAlternatingRowBrush"] = null;
        }
    }

    public void Exit(object sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime) {
            desktopLifetime.Shutdown();
        }
    }
}