using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using RTSharp.Shared.Controls;
using RTSharp.ViewModels;

namespace RTSharp;

public partial class ManagePluginsWindow : VmWindow<ManagePluginsWindowViewModel>
{
    public ManagePluginsWindow()
    {
        InitializeComponent();
    }
}