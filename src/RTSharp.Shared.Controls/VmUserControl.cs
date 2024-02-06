using Avalonia;
using Avalonia.Controls;

namespace RTSharp.Shared.Controls
{
    public class VmUserControl<T> : UserControl, IVmContentControl<T>
    {
        public T? ViewModel {
            get {
                return (T?)DataContext;
            }
            set {
                DataContext = value;
            }
        }

        public void BindViewModelActions(Action<T> FxBind, Action<T>? FxUnbind) => ((IVmContentControl<T>)this).BindViewModelActions(this, FxBind, FxUnbind);

        public void BindViewModelActions(Action<T> FxBind) => ((IVmContentControl<T>)this).BindViewModelActions(this, FxBind);

        Action<T> IVmContentControl<T>.FxBind { get; set; }
        Action<T>? IVmContentControl<T>.FxUnbind { get; set; }
        T? IVmContentControl<T>.PreviousViewModel { get; set; } = default;
    }
}
