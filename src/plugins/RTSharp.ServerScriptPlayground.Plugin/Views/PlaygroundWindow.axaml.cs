using AvaloniaEdit.Indentation.CSharp;
using AvaloniaEdit;

using RTSharp.ServerScriptPlayground.Plugin.ViewModels;
using RTSharp.Shared.Controls;

namespace RTSharp.ServerScriptPlayground.Plugin.Views;

public partial class PlaygroundWindow : VmWindow<PlaygroundWindowViewModel>
{
    public PlaygroundWindow()
    {
        InitializeComponent();

        Editor.TextArea.IndentationStrategy = new CSharpIndentationStrategy();
    }
}