using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System;
using RTSharp.Shared.Controls.ViewModels;

namespace RTSharp.Shared.Controls.Views
{
    public partial class WaitingBox : Window, IProgress<(int Progress, string Description)>
    {
        public WaitingBox()
            : this("Operation in progress...", "Please wait while the operation is in progress.", WAITING_BOX_ICON.WIN10_INFO)
        {
        }

        public WaitingBox(string Text, string Description, WAITING_BOX_ICON Icon)
        {
            DataContext = new WaitingBoxViewModel(Text, Description, Icon);

            InitializeComponent();
            Closing += EvClosing;
        }

        public void Report((int Progress, string Description) In)
        {
            var data = (WaitingBoxViewModel)DataContext;

            data.Progress = In.Progress;
            data.Description = In.Description;
        }

        public new void Close()
        {
            Closing -= EvClosing;
            base.Close();
        }

        private void EvClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
        }
    }
}
