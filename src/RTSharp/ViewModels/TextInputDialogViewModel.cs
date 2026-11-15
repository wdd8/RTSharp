using System;
using System.Collections.Generic;
using System.Linq;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RTSharp.ViewModels;

public partial class TextInputField : ObservableObject
{
    /// <summary>
    /// Label of the text box, with an access key, for example "_Name:"
    /// </summary>
    public required string Caption { get; init; }

    [ObservableProperty]
    public partial string Text { get; set; } = "";
}

public partial class TextInputDialogViewModel : ObservableObject
{
    public required IReadOnlyList<TextInputField> Fields { get; init; }

    public Action<string[]?> CloseWithResult { get; set; } = null!; // view set

    [RelayCommand]
    public void Ok() => CloseWithResult(Fields.Select(x => x.Text).ToArray());

    [RelayCommand]
    public void Cancel() => CloseWithResult(null);
}
