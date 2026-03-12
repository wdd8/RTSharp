using Avalonia.Controls;
using Avalonia.Controls.DataGridFiltering;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using System.Diagnostics.CodeAnalysis;
using System.Windows.Input;

namespace RTSharp.Shared.Controls.DataGridFilters;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties)]
public partial class TextFilterContext : ObservableObject, IFilterTextContext
{
    private readonly DataGridColumnDefinition Column;
    private readonly FilteringModel FilteringModel;
    private readonly FilteringOperator FilteringOperator;
    private readonly Func<object?, string?, bool>? FxEquals;

    public TextFilterContext(string label, FilteringModel filteringModel, DataGridColumnDefinition column, FilteringOperator filteringOperator, Func<object?, string?, bool>? fxEquals = null)
    {
        Label = label;
        Column = column;
        FilteringModel = filteringModel;
        FilteringOperator = filteringOperator;
        FxEquals = fxEquals;
        ApplyCommand = new RelayCommand(ApplyFilter);
        ClearCommand = new RelayCommand(ClearFilter);
    }

    public string Label { get; }

    [ObservableProperty]
    public partial string? Text { get; set; }

    public ICommand ApplyCommand { get; }
    public ICommand ClearCommand { get; }

    public void ApplyFilter()
    {
        if (string.IsNullOrWhiteSpace(Text)) {
            FilteringModel.Remove(Column);
            return;
        }

        FilteringModel.SetOrUpdate(new FilteringDescriptor(
            columnId: Column,
            @operator: FilteringOperator,
            predicate: FilteringOperator == FilteringOperator.Custom && FxEquals != null ? x => FxEquals(x, Text) : null,
            value: Text,
            stringComparison: StringComparison.OrdinalIgnoreCase));
    }

    public void ClearFilter()
    {
        Text = "";
        FilteringModel.Remove(Column);
    }
}
