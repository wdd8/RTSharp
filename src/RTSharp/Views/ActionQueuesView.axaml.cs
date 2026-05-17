using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;

using RTSharp.Shared.Abstractions.Client;

namespace RTSharp.Views
{
    public partial class ActionQueuesView : UserControl
    {
        public ActionQueuesView()
        {
            InitializeComponent();

            QueueRepeater.ItemTemplate = new FuncDataTemplate<IActionQueueRenderer>((renderer, _) => {
                var display = renderer?.CreateDisplay();

                return new ContentControl {
                    Content = display
                };
            });
        }
    }
}
