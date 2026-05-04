using RTSharp.Shared.Abstractions.Client;
using RTSharp.ViewModels.Statistics;

namespace RTSharp.Views.Statistics;

public partial class StatisticsView : VmUserControl<StatisticsViewModel>
{
    public StatisticsView()
    {
        InitializeComponent();

        this.AttachedToVisualTree += (sender, e) => {
            ViewModel?.RunTimer();
        };
        this.DetachedFromVisualTree += (sender, e) => {
            ViewModel?.Timer?.Dispose();
        };
    }
}
