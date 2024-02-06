using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;

namespace RTSharp.Shared.Controls.ViewModels
{
    public class DockableDocumentWrapperViewModel : Document
    {
        public IDock Dockable { get; }

        public DockableDocumentWrapperViewModel(IDock Dockable)
        {
            this.Dockable = Dockable;
        }
    }
}
