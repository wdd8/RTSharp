using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;

using RTSharp.Shared.Abstractions.DataProvider;

namespace RTSharp.Shared.Controls.Views;

public partial class DataProviderSelection : UserControl
{
    public static readonly StyledProperty<IList<IDataProvider>> DataProvidersProperty =
        AvaloniaProperty.Register<DataProviderSelection, IList<IDataProvider>>(nameof(DataProviders));

    public static readonly StyledProperty<IDataProvider?> SelectedProviderProperty =
        AvaloniaProperty.Register<DataProviderSelection, IDataProvider?>(nameof(SelectedProvider), defaultBindingMode: BindingMode.TwoWay);

    public IList<IDataProvider> DataProviders
    {
        get => GetValue(DataProvidersProperty);
        set => SetValue(DataProvidersProperty, value);
    }

    public IDataProvider? SelectedProvider
    {
        get => GetValue(SelectedProviderProperty);
        set => SetValue(SelectedProviderProperty, value);
    }

    public DataProviderSelection()
    {
        InitializeComponent();
    }
}
