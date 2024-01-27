using Avalonia.Controls;
using Avalonia.Platform.Storage;

using Avalonia.Styling;
using Avalonia;
using Avalonia.Interactivity;

namespace RTSharp.Shared.Controls.Views
{
    public partial class FolderSelection : UserControl, IStyleable
    {
        public FolderSelection()
        {
            InitializeComponent();
        }

        #region Value
        /// <summary>
        /// Defines the <see cref="Value"/> property.
        /// </summary>
        public static readonly StyledProperty<string> ValueProperty = AvaloniaProperty.Register<FolderSelection, string>(nameof(Value));

        /// <summary>
        /// Gets or sets path text
        /// </summary>
        public string Value {
            get { return GetValue(ValueProperty); }
            set { SetValue(ValueProperty, value); }
        }
        #endregion Value

        Type IStyleable.StyleKey => typeof(FolderSelection);

        public async void EvBrowse(object sender, RoutedEventArgs e)
        {
            StyledElement? wnd = Parent;
            while (wnd != null && wnd is not Window) {
                wnd = wnd.Parent;
            }
            if (wnd == null)
                return;

            var dir = await ((Window)wnd).StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions() {
                AllowMultiple = false,
                //SuggestedStartLocation = ??
            });

#pragma warning disable CA1826
            Value = dir?.FirstOrDefault()?.Path?.LocalPath;
#pragma warning restore CA1826
        }
    }
}
