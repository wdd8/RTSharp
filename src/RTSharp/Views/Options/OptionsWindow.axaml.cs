using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using RTSharp.Shared.Abstractions.Client;
using RTSharp.ViewModels.Options;

namespace RTSharp.Views.Options
{
    public partial class OptionsWindow : VmWindow<OptionsWindowViewModel>
    {
        public OptionsWindow()
        {
            InitializeComponent();
        }
    }
}
