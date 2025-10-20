using Hearthstone_Deck_Tracker;
using HSIntel.Core.Config;
using HSIntel.Core.Interfaces;
using HSIntel.Core.Services;
using HSIntel.Engine.Services;

namespace Hearthstone_Deck_Tracker.HSIntel
{
	internal static class HSIntelBootstrapper
	{
		private static readonly object Sync = new();
		private static HDTEventBinder? _binder;
		private static IHdtEventSource? _eventSource;
		private static OpponentStateBuilder? _opponentState;
		private static StateBuilder? _stateBuilder;
		private static HdtGameStateSource? _gameStateSource;
		private static DecisionEngineCoordinator? _decisionEngine;

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

		public static StateBuilder StateBuilder
		{
			get
			{
				var binder = EventBinder;

				lock(Sync)
				{
					if(_stateBuilder == null)
					{
						_gameStateSource ??= new HdtGameStateSource();
						_stateBuilder = new StateBuilder(binder, _gameStateSource);
						_stateBuilder.Initialize();
					}

					return _stateBuilder;
				}
			}
		}

		public static DecisionEngineCoordinator DecisionEngine
		{
			get
			{
				var stateBuilder = StateBuilder;

				lock(Sync)
				{
					if(_decisionEngine == null)
					{
						_decisionEngine = new DecisionEngineCoordinator(stateBuilder, () => Config.Instance.HSIntel ?? new HSIntelConfig());
						_decisionEngine.Start();
					}

					return _decisionEngine;
				}
			}
		}

		public static void EnsureInitialized()
		{
			_ = EventBinder;
			_ = OpponentState;
			_ = StateBuilder;
			_ = DecisionEngine;
		}

		public static void Shutdown()
		{
			lock(Sync)
			{
				if(_binder == null)
					return;

				_decisionEngine?.Dispose();
				_decisionEngine = null;

				_stateBuilder?.Dispose();
				_stateBuilder = null;

				_opponentState?.Dispose();
				_opponentState = null;

				_binder.Dispose();
				_binder = null;
				_eventSource = null;
				_gameStateSource = null;
			}
		}
	}
}
