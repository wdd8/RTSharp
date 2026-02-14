using RTSharp.Shared.Controls;
using RTSharp.ViewModels.Statistics;

namespace RTSharp.Views.Statistics
{
    public partial class StatisticsWindow : VmWindow<StatisticsWindowViewModel>
    {
        public StatisticsWindow()
        {
            InitializeComponent();

            this.Opened += (sender, e) => {
                ViewModel?.RunTimer();
            };
            this.Closed += (sender, e) => {
                ViewModel?.Timer.Dispose();
            };
        }
    }
}
