using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;

namespace RTSharp.Shared.Controls.Views
{
    public partial class GroupBox : UserControl, IStyleable
    {
#region Label
        private string _label;

        /// <summary>
        /// Defines the <see cref="Label"/> property.
        /// </summary>
        public static readonly DirectProperty<GroupBox, string> LabelProperty =
            AvaloniaProperty.RegisterDirect<GroupBox, string>(
                nameof(Label),
                o => o.Label,
                (o, v) => o.Label = v);

        /// <summary>
        /// Gets or sets header text
        /// </summary>
        public string Label {
            get { return _label; }
            set { SetAndRaise(LabelProperty, ref _label, value); }
        }
#endregion Label
#region RadioButtonGroup
        private string? _radioButtonGroup;

        /// <summary>
        /// Defines the <see cref="RadioButton"/> property.
        /// </summary>
        public static readonly DirectProperty<GroupBox, string> RadioButtonGroupProperty =
            AvaloniaProperty.RegisterDirect<GroupBox, string>(
                nameof(RadioButtonGroup),
                o => o.RadioButtonGroup,
                (o, v) => o.RadioButtonGroup = v);

        /// <summary>
        /// Gets or sets visibility of a radio button
        /// </summary>
        public string RadioButtonGroup {
            get { return _radioButtonGroup; }
            set {
                SetAndRaise(RadioButtonGroupProperty, ref _radioButtonGroup, value);
                SetAndRaise(RadioButtonEnabledProperty, ref _radioButtonEnabled, _radioButtonGroup != null);
            }
        }
#endregion RadioButtonGroup
#region RadioButtonEnabled
        public static readonly DirectProperty<GroupBox, bool> RadioButtonEnabledProperty =
            AvaloniaProperty.RegisterDirect<GroupBox, bool>(
                nameof(_radioButtonEnabled),
                o => o._radioButtonEnabled);

        private bool _radioButtonEnabled;

        public bool RadioButtonEnabled {
            get { return _radioButtonEnabled; }
        }
        #endregion RadioButtonEnabled
#region IsRadioButtonChecked
        /// <summary>
        /// Defines the <see cref="IsRadioButtonChecked"/> property.
        /// </summary>
        public static readonly StyledProperty<bool?> IsRadioButtonCheckedProperty =
            RadioButton.IsCheckedProperty.AddOwner<GroupBox>();

        /// <summary>
        /// Gets or sets checked state of radio button
        /// </summary>
        public bool? IsRadioButtonChecked {
            get { return GetValue(IsRadioButtonCheckedProperty); }
            set { SetValue(IsRadioButtonCheckedProperty, value); }
        }
        #endregion IsRadioButtonChecked
#region CheckBoxEnabled
        private bool _checkBoxEnabled;

        public static readonly DirectProperty<GroupBox, bool> CheckBoxEnabledProperty =
            AvaloniaProperty.RegisterDirect<GroupBox, bool>(
                nameof(CheckBoxEnabled),
                o => o.CheckBoxEnabled,
                (o, v) => o.CheckBoxEnabled = v);

        /// <summary>
        /// Gets or sets visibility of a check box
        /// </summary>
        public bool CheckBoxEnabled {
            get { return _checkBoxEnabled; }
            set { SetAndRaise(CheckBoxEnabledProperty, ref _checkBoxEnabled, value); }
        }
#endregion CheckBoxEnabled
#region IsCheckBoxChecked
        /// <summary>
        /// Defines the <see cref="IsCheckBoxChecked"/> property.
        /// </summary>
        public static readonly StyledProperty<bool?> IsCheckBoxCheckedProperty =
            CheckBox.IsCheckedProperty.AddOwner<GroupBox>();

        /// <summary>
        /// Gets or sets checked state of check box
        /// </summary>
        public bool? IsCheckBoxChecked {
            get { return GetValue(IsCheckBoxCheckedProperty); }
            set { SetValue(IsCheckBoxCheckedProperty, value); }
        }
#endregion IsCheckBoxChecked

        /// <summary>
        /// Defines the <see cref="Background"/> property.
        /// </summary>
        public static readonly StyledProperty<IBrush> BorderBackgroundProperty =
            AvaloniaProperty.Register<GroupBox, IBrush>(nameof(BorderBackground));

        /// <summary>
        /// Gets or sets a brush with which to paint the Header background.
        /// </summary>
        public IBrush BorderBackground {
            get { return GetValue(BorderBackgroundProperty); }
            set { SetValue(BorderBackgroundProperty, value); }
        }

        Type IStyleable.StyleKey => typeof(GroupBox);

        public void EvTextBlockTapped(object e, TappedEventArgs args)
        {
            IsRadioButtonChecked = true;
        }

        public GroupBox()
        {
            InitializeComponent();
        }
    }
}
