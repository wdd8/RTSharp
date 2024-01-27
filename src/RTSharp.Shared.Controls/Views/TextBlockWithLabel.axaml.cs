using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;

namespace RTSharp.Shared.Controls.Views
{
	public partial class TextBlockWithLabel : UserControl, IStyleable
	{
		public TextBlockWithLabel()
		{
			InitializeComponent();
		}

#region Label
		private string _label = "Label: ";

		/// <summary>
		/// Defines the <see cref="Label"/> property.
		/// </summary>
		public static readonly DirectProperty<TextBlockWithLabel, string> LabelProperty =
			AvaloniaProperty.RegisterDirect<TextBlockWithLabel, string>(
				nameof(Label),
				o => o.Label,
				(o, v) => o.Label = v);

		/// <summary>
		/// Gets or sets label text
		/// </summary>
		public string Label {
			get { return _label; }
			set { SetAndRaise(LabelProperty, ref _label, value); }
		}
#endregion Label

#region Text
		private string _text = "Text";

		/// <summary>
		/// Defines the <see cref="Text"/> property.
		/// </summary>
		public static readonly DirectProperty<TextBlockWithLabel, string> TextProperty =
			AvaloniaProperty.RegisterDirect<TextBlockWithLabel, string>(
				nameof(Text),
				o => o.Text,
				(o, v) => o.Text = v);

		/// <summary>
		/// Gets or sets text text
		/// </summary>
		public string Text {
			get { return _text; }
			set { SetAndRaise(TextProperty, ref _text, value); }
		}
#endregion Text
	}
}
