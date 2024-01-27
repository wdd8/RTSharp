using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.DependencyInjection;

using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

using RTSharp.Core.Services.Auxiliary;

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
            Servers = new ObservableCollection<Models.Server>();
            using var scope = Core.ServiceProvider.CreateScope();
            var config = scope.ServiceProvider.GetRequiredService<Core.Config>();

            foreach (var (id, server) in config.Servers.Value) {
                Servers.Add(new Models.Server {
                    ServerId = id,
                    Host = server.Host,
                    AuxiliaryServicePort = server.AuxiliaryServicePort
                });
            }
        }

        [RelayCommand]
        public async Task Test(Models.Server Server)
        {
            try {
                var aux = new AuxiliaryService(Server.ServerId);

                await aux.Ping();

                var msgBox = MessageBoxManager.GetMessageBoxStandard("RT#", "Connection successful", ButtonEnum.Ok, Icon.Success);
                await msgBox.ShowAsync();
            } catch (Exception ex) {
                Log.Logger.Error(ex, "Ping to auxiliary service has failed");

                var msgBox = MessageBoxManager.GetMessageBoxStandard("RT#", "Connection failed", ButtonEnum.Ok, Icon.Error);
                await msgBox.ShowAsync();
            }
        }
    }
}
