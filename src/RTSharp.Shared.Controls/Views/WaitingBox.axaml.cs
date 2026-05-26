using Avalonia.Controls;
using RTSharp.Shared.Controls.ViewModels;

namespace RTSharp.Shared.Controls.Views
{
    public partial class WaitingBox : Window
    {
        public WaitingBox()
            : this("Operation in progress...", "Please wait while the operation is in progress.", BuiltInIcons.WIN10_INFO)
        {
        }

        public WaitingBox(string Text, string Description, BuiltInIcons Icon)
        {
            DataContext = new WaitingBoxViewModel(Text, Description, Icon);

            InitializeComponent();
            Closing += EvClosing;
        }

        public void Report((int Progress, string Description) In)
        {
            if (DataContext is not WaitingBoxViewModel data)
                return;

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
