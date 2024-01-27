﻿using Avalonia.Data.Converters;
using Avalonia.Platform;
using Avalonia;

using System.Globalization;
using System.Reflection;
using Avalonia.Media.Imaging;

namespace RTSharp.Shared.Controls.Converters
{
	/// <summary>
	/// <para>
	/// Converts a string path to a bitmap asset.
	/// </para>
	/// <para>
	/// The asset must be in the same assembly as the program. If it isn't,
	/// specify "avares://<assemblynamehere>/" in front of the path to the asset.
	/// </para>
	/// </summary>
	public class BitmapAssetValueConverter : IValueConverter
	{
		public static BitmapAssetValueConverter Instance = new BitmapAssetValueConverter();

		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value == null)
				return null;

			if (value is string rawUri && targetType.IsAssignableFrom(typeof(Bitmap))) {
				Uri uri;

				if (rawUri.StartsWith("avares://")) {
					uri = new Uri(rawUri);
					var asset = AssetLoader.Open(uri);

					return new Bitmap(asset);
				} else {
					return new Bitmap(rawUri);
				}
			}

			throw new NotSupportedException();
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			throw new NotSupportedException();
		}
	}
}
