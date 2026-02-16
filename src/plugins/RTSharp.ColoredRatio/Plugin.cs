using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.Client;

using System;
using System.Threading.Tasks;

namespace RTSharp.ColoredRatio.Plugin;

public class Plugin : BasePlugin
{
    public override string Name => "Colored ratio";

    public override string Description => "Makes ratio cells colored";

    public override string Author => "RTSharp";

    public override Shared.Abstractions.Version Version => new Shared.Abstractions.Version("1.0.0", 1, 0, 0);

    public override int CompatibleMajorVersion => 0;

    public override Guid GUID { get; } = new Guid("55D84EE8-6FD9-4AAB-8192-3043CC045491");

    public override PluginCapabilities Capabilities => new PluginCapabilities(
        HasSettingsWindow: false
    );

    public override IPluginHost Host { get; set; }

    IDisposable Hook;

    public override Task<dynamic> CustomAccess(dynamic In) => Task.FromResult<dynamic>(null);

    public override Task<IPlugin> Init(IPluginHost Host, Action<(string Status, float Percentage)> Progress)
    {
        this.Host = Host;
        Progress(("Initializing...", 0f));

        Hook = Host.HookTorrentListingEvCellPrepared((sender, e) => {
            var cell = (TreeDataGridCell)e.Cell;
            var dataGrid = (TreeDataGrid)sender;

            if ((string)dataGrid.Columns[e.ColumnIndex].Header != "Ratio") {
                return;
            }
            
            var torrent = (Models.Torrent)cell.DataContext;
            var ratio = torrent.Ratio;

            Color color;
            if (ratio < 1)
                color = Color.FromRgb((byte)(255 - (int)(ratio / 1 * 100)), 0, 0);
            else if (ratio >= 1 && ratio <= 5)
                color = Color.FromRgb(0, (byte)(100 + (int)((ratio - 1) / 4 * 155)), 0);
            else if (ratio > 5 && ratio <= 25)
                color = Color.FromRgb((byte)(255 - (int)((ratio - 5) / 20 * 245)), 0, (byte)(255 - (int)((ratio - 5) / 25 * 245)));
            else if (ratio == Double.MaxValue)
                color = Color.FromRgb(0, 0, 0);
            else
                color = Color.FromRgb(255, 255, 255);

            cell.Foreground = new SolidColorBrush(color);
            if (ratio > 5)
                cell.Background = Brushes.Black;
            else
                cell.Background = null;
        });

        Progress(("Done", 100f));

        return Task.FromResult((IPlugin)this);
    }

    public override Task ShowPluginSettings(object ParentWindow) => throw new NotImplementedException();

    public override Task Unload()
    {
        Hook?.Dispose();

        return Task.CompletedTask;
    }
}