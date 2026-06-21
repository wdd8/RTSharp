using RTSharp.Shared.Abstractions.Client;
using RTSharp.ViewModels;

namespace RTSharp.Views;

public partial class DataProviderLogsView : VmUserControl<DataProviderLogsViewModel>
{
    public DataProviderLogsView()
    {
        InitializeComponent();

        BindViewModelActions(_ => { }, vm => vm?.Dispose());
    }
}
