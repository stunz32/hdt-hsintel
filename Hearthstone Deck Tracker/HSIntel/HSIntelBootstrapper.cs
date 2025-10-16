using HSIntel.Core.Interfaces;
using HSIntel.Core.Services;

namespace Hearthstone_Deck_Tracker.HSIntel
{
	internal static class HSIntelBootstrapper
	{
		private static readonly object Sync = new();
		private static HDTEventBinder? _binder;
		private static IHdtEventSource? _eventSource;
		private static OpponentStateBuilder? _opponentState;

		public static HDTEventBinder EventBinder
		{
			get
			{
				lock(Sync)
				{
					if(_binder != null)
						return _binder;

					_eventSource = new HdtEventSource();
					_binder = new HDTEventBinder(_eventSource);
					_binder.Initialize();
					return _binder;
				}
			}
		}

		public static OpponentStateBuilder OpponentState
		{
			get
			{
				var binder = EventBinder;

				lock(Sync)
				{
					if(_opponentState == null)
						_opponentState = new OpponentStateBuilder(binder);

					return _opponentState;
				}
			}
		}

		public static void EnsureInitialized()
		{
			_ = EventBinder;
			_ = OpponentState;
		}

		public static void Shutdown()
		{
			lock(Sync)
			{
				if(_binder == null)
					return;

				_opponentState?.Dispose();
				_opponentState = null;

				_binder.Dispose();
				_binder = null;
				_eventSource = null;
			}
		}
	}
}
