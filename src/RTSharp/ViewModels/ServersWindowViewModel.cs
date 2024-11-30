using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

using Serilog;

using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace RTSharp.ViewModels
{
    public partial class ServersWindowViewModel : ObservableObject
    {
        [ObservableProperty]
        public ObservableCollection<Models.Server> servers;

        [ObservableProperty]
        public Models.Server selectedServer;

        public ServersWindowViewModel()
        {
            Servers = [];

            foreach (var (id, server) in Core.Servers.Value) {
                Servers.Add(new Models.Server {
                    ServerId = id,
                    Host = server.Host,
                    DaemonPort = server.Port
                });
            }
        }

        [RelayCommand]
        public async Task Test(Models.Server Server)
        {
            try {
                await Core.Servers.Value[Server.ServerId].Ping(default);

                var msgBox = MessageBoxManager.GetMessageBoxStandard("RT#", "Connection successful", ButtonEnum.Ok, Icon.Success);
                await msgBox.ShowAsync();
            } catch (Exception ex) {
                Log.Logger.Error(ex, "Ping to daemon has failed");

                var msgBox = MessageBoxManager.GetMessageBoxStandard("RT#", "Connection failed", ButtonEnum.Ok, Icon.Error);
                await msgBox.ShowAsync();
            }
        }
    }
}
