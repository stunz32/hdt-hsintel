using System.Windows.Controls;
using Hearthstone_Deck_Tracker.Utility;
using Hearthstone_Deck_Tracker.Utility.Extensions;

namespace Hearthstone_Deck_Tracker.FlyoutControls.Options.HSIntel
{
	public partial class UpstreamStatus : UserControl
	{
		public UpstreamStatus()
		{
			VersionString = Helper.GetCurrentVersion().ToVersionString(true);
			InitializeComponent();
			DataContext = this;
		}

		public string VersionString { get; }
	}
}
