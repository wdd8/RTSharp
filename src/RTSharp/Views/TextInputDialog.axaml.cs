using System.Linq;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.VisualTree;

using RTSharp.Shared.Abstractions.Client;
using RTSharp.ViewModels;

namespace RTSharp.Views;

/// <summary>
/// Asks for one or more lines of text, with OK and Cancel
/// </summary>
public partial class TextInputDialog : VmWindow<TextInputDialogViewModel>
{
    public TextInputDialog()
    {
        InitializeComponent();

        BindViewModelActions(vm => {
            vm!.CloseWithResult = Close;
        });

        Opened += (_, _) => {
            if (this.GetVisualDescendants().OfType<TextBox>().FirstOrDefault() is { } first) {
                first.Focus();
                first.SelectAll();
            }
        };
    }

    /// <param name="Fields">Caption with an access key, for example "_Name:", and initial text of each text box</param>
    /// <returns>The text of each field in order, or null when cancelled</returns>
    public static Task<string[]?> Prompt(Window Owner, string Title, params (string Caption, string Text)[] Fields) =>
        new TextInputDialog {
            Title = Title,
            ViewModel = new TextInputDialogViewModel {
                Fields = [.. Fields.Select(x => new TextInputField { Caption = x.Caption, Text = x.Text })]
            }
        }.ShowDialog<string[]?>(Owner);
}
