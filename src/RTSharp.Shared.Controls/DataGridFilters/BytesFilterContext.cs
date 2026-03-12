using Avalonia.Controls;
using Avalonia.Controls.DataGridFiltering;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using System.Diagnostics.CodeAnalysis;
using System.Windows.Input;

namespace RTSharp.Shared.Controls.DataGridFilters;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties)]
public sealed class BytesFilterContext : ObservableObject, IFilterTextContext
{
    private string? _text;
    private readonly Action<string?> _apply;
    private readonly Action _clear;

    public BytesFilterContext(string label, Action<string?> apply, Action clear)
    {
        Label = label;
        _apply = apply;
        _clear = clear;
        ApplyCommand = new RelayCommand(() => _apply(Text));
        ClearCommand = new RelayCommand(() => _clear());
    }

    public string Label { get; }

    public string? Text {
        get => _text;
        set => SetProperty(ref _text, value);
    }

    public ICommand ApplyCommand { get; }
    public ICommand ClearCommand { get; }

    public static void ApplyBytesFilter(FilteringModel FilteringModel, DataGridColumnDefinition column, string propertyPath, string? text, FilteringOperator filteringOperator)
    {
        if (string.IsNullOrWhiteSpace(text)) {
            FilteringModel.Remove(column);
            return;
        }

        FilteringModel.SetOrUpdate(new FilteringDescriptor(
            columnId: column,
            @operator: filteringOperator,
            propertyPath: propertyPath,
            value: Convert.FromHexString(text),
            stringComparison: StringComparison.OrdinalIgnoreCase));
    }

    public static void ClearFilter(FilteringModel FilteringModel, DataGridColumnDefinition columnId, Action reset)
    {
        reset();
        FilteringModel.Remove(columnId);
    }
}
