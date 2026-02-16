using AvaloniaEdit.Indentation.CSharp;

using RTSharp.ServerScriptPlayground.Plugin.ViewModels;
using RTSharp.Shared.Abstractions.Client;

namespace RTSharp.ServerScriptPlayground.Plugin.Views;

public partial class PlaygroundWindow : VmWindow<PlaygroundWindowViewModel>
{
    public PlaygroundWindow()
    {
        InitializeComponent();

        Editor.TextArea.IndentationStrategy = new CSharpIndentationStrategy();
    }
}