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
        public partial ObservableCollection<Models.Server> Servers { get; set; }

        [ObservableProperty]
        public partial Models.Server SelectedServer { get; set; }

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

                var msgBox = MessageBoxManager.GetMessageBoxStandard(title: "RT#", text: "Connection successful", @enum: ButtonEnum.Ok, icon: Icon.Success);
                await msgBox.ShowAsync();
            } catch (Exception ex) {
                Log.Logger.Error(ex, "Ping to daemon has failed");

                var msgBox = MessageBoxManager.GetMessageBoxStandard(title: "RT#", text: "Connection failed", @enum: ButtonEnum.Ok, icon: Icon.Error);
                await msgBox.ShowAsync();
            }
        }
    }
}
