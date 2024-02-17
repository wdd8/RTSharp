using Avalonia;

namespace RTSharp.Shared.Controls
{
    public interface IVmContentControl<T>
    {
        T? ViewModel { get; set; }

        protected Action<T> FxBind { get; set; }
        protected Action<T>? FxUnbind { get; set; }
        protected T? PreviousViewModel { get; set; }

        public void BindViewModelActions(StyledElement El, Action<T> FxBind) => BindViewModelActions(El, FxBind, null);

        public void BindViewModelActions(StyledElement El, Action<T> FxBind, Action<T>? FxUnbind)
        {
            ArgumentNullException.ThrowIfNull(FxBind, nameof(FxBind));

            this.FxBind = FxBind;
            this.FxUnbind = FxUnbind;
            El.DataContextChanged += VmContentControl_DataContextChanged;
        }

        private void VmContentControl_DataContextChanged(object? sender, EventArgs e)
        {
            if (PreviousViewModel != null && FxUnbind != null)
                FxUnbind(PreviousViewModel);
            if (ViewModel != null)
                FxBind(ViewModel);

            PreviousViewModel = ViewModel;

            if (ViewModel is IContextPopulatedNotifyable notifyable) {
                notifyable.OnContextPopulated();
            }
        }
    }
}
