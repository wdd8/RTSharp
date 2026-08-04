using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.Extensions.DependencyInjection;

namespace RTSharp.ViewModels.Options.Pages;

public partial class NetworkPageViewModel : ObservableObject, ISettingsLoadable
{
    [ObservableProperty]
    public partial bool ProxyEnabled { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProxyAddressValid))]
    public partial string ProxyAddress { get; set; } = "";

    [ObservableProperty]
    public partial string ProxyUsername { get; set; } = "";

    [ObservableProperty]
    public partial string ProxyPassword { get; set; } = "";

    public bool ProxyAddressValid => ProxyAddress.Length == 0 || Core.GlobalProxy.TryParseAddress(ProxyAddress, out _);

    public NetworkPageViewModel()
    {
        if (!Avalonia.Controls.Design.IsDesignMode)
            Load();
    }

    public void Load()
    {
        using var scope = Core.ServiceProvider.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<Core.Config>();
        var proxy = config.Proxy.Value;

        ProxyEnabled = proxy.Enabled;
        ProxyAddress = proxy.Address;
        ProxyUsername = proxy.Username;
        ProxyPassword = proxy.Password;
    }

    public void ApplyToConfig(Core.Config config)
    {
        var proxy = config.Proxy.Value;
        proxy.Enabled = ProxyEnabled;
        proxy.Address = ProxyAddress;
        proxy.Username = ProxyUsername;
        proxy.Password = ProxyPassword;
    }
}
