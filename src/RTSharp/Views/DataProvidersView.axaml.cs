using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

using RTSharp.Shared.Abstractions.Client;
using RTSharp.ViewModels;

namespace RTSharp.Views;

public partial class DataProvidersView : VmUserControl<DataProvidersViewModel>
{
    public DataProvidersView()
    {
        InitializeComponent();
        list.PointerPressed += EvListPointerPressedClearSelection;
    }

    private void EvListPointerPressedClearSelection(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed)
            return;
        if (e.Source is not Visual source)
            return;
        if (source is ListBoxItem || source.FindAncestorOfType<ListBoxItem>() != null)
            return;
        list.SelectedItem = null;
    }
}
