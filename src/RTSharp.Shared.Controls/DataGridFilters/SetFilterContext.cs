using Avalonia.Controls;
using Avalonia.Controls.DataGridFiltering;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using System.Diagnostics.CodeAnalysis;
using System.Windows.Input;

namespace RTSharp.Shared.Controls.DataGridFilters;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties)]
public partial class SetFilterContext<T> : ObservableObject
    where T : IComparable
{
    private readonly DataGridColumnDefinition Column;
    private readonly FilteringModel FilteringModel;
    private readonly FilteringOperator FilteringOperator;
    private readonly Func<object?, IReadOnlyList<T?>, bool>? FxEquals;

    public SetFilterContext(string label, FilteringModel filteringModel, DataGridColumnDefinition column, FilteringOperator filteringOperator, Func<object?, IReadOnlyList<T?>, bool>? fxEquals = null)
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

    public SortedSet<SelectedOption<T>> Options { get; set; } = [];

    public ICommand ApplyCommand { get; }

    public ICommand ClearCommand { get; }

    public void ApplyFilter()
    {
        if (Options.All(x => !x.IsSelected)) {
            FilteringModel.Remove(Column);
            return;
        }

        var values = Options.Where(x => x.IsSelected).Select(x => x.Display).ToList().AsReadOnly();

        FilteringModel.SetOrUpdate(new FilteringDescriptor(
            columnId: Column,
            @operator: FilteringOperator,
            predicate: FilteringOperator == FilteringOperator.Custom && FxEquals != null ? x => FxEquals(x, values) : null,
            values: [.. values.Cast<object>()],
            stringComparison: StringComparison.Ordinal));
    }

    public void ClearFilter()
    {
        foreach (var options in Options) {
            options.IsSelected = false;
        }
        FilteringModel.Remove(Column);
    }
}
