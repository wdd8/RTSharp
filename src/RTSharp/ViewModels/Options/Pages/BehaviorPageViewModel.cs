using System;

using CommunityToolkit.Mvvm.ComponentModel;

namespace RTSharp.ViewModels.Options.Pages
{
	public partial class BehaviorPageViewModel : ObservableObject, ISettingsLoadable
	{
		[ObservableProperty]
		public TimeSpan filesPollingInterval;
	}
}
