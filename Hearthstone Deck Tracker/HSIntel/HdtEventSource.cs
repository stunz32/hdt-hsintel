using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;
using HearthDb.Enums;
using Hearthstone_Deck_Tracker;
using Hearthstone_Deck_Tracker.API;
using Hearthstone_Deck_Tracker.Enums;
using Hearthstone_Deck_Tracker.Hearthstone;
using Hearthstone_Deck_Tracker.Hearthstone.Entities;
using Hearthstone_Deck_Tracker.Utility;
using Hearthstone_Deck_Tracker.Hearthstone.Secrets;
using HSIntel.Core.Interfaces;
using HSIntel.Core.Models.Events;
using HSIntel.Core.Models;
using HSIntel.Core.Models.Events.Raw;

namespace Hearthstone_Deck_Tracker.HSIntel
{
	internal sealed class HdtEventSource : IHdtEventSource
	{
		private readonly DispatcherTimer _updateTimer;
		private Action<RawUpdateTickEvent>? _updateHandlers;
		private Action<RawOpponentMulliganSummaryEvent>? _opponentMulliganHandlers;
		private Action<RawCardCreatedEvent>? _opponentCreatedHandlers;
		private Action? _secretsChangedHandlers;
		private bool _opponentMulliganEmitted;
		private bool _disposed;
		private readonly HashSet<int> _knownCreatedEntities = new HashSet<int>();

		public HdtEventSource()
		{
			_updateTimer = new DispatcherTimer(DispatcherPriority.Background)
			{
				Interval = TimeSpan.FromMilliseconds(100)
			};
			_updateTimer.Tick += OnUpdateTick;

			GameEvents.OnGameStart.Add(ResetOpponentMulliganState);
			GameEvents.OnGameEnd.Add(ResetOpponentMulliganState);

			// Fire when SecretsManager updates
			Core.Game.SecretsManager.OnSecretsChanged += _ => _secretsChangedHandlers?.Invoke();
		}

		public void RegisterGameStarted(Action handler)
		{
			if(handler == null)
				return;
			GameEvents.OnGameStart.Add(() => handler());
		}

		public void RegisterGameEnded(Action handler)
		{
			if(handler == null)
				return;
			GameEvents.OnGameEnd.Add(() => handler());
		}

		public void RegisterTurnStarted(Action<RawTurnStartEvent> handler)
		{
			if(handler == null)
				return;
			GameEvents.OnTurnStart.Add(player =>
			{
				var side = player == ActivePlayer.Player ? ParticipantSide.Friendly : ParticipantSide.Opponent;
				var turnNumber = Core.Game.GetTurnNumber();
				handler(new RawTurnStartEvent(side, turnNumber, DateTimeOffset.UtcNow));
			});
		}

		public void RegisterPlayerCardDrawn(Action<RawCardEvent> handler)
		{
			if(handler == null)
				return;
			GameEvents.OnPlayerDraw.Add(card => handler(CreateCardEvent(card, ParticipantSide.Friendly)));
		}

		public void RegisterOpponentCardDrawn(Action<RawCardEvent> handler)
		{
			if(handler == null)
				return;
			GameEvents.OnOpponentDraw.Add(() => handler(CreateUnknownCardEvent(ParticipantSide.Opponent)));
		}

		public void RegisterPlayerCardPlayed(Action<RawCardEvent> handler)
		{
			if(handler == null)
				return;
			GameEvents.OnPlayerPlay.Add(card => handler(CreateCardEvent(card, ParticipantSide.Friendly)));
		}

		public void RegisterOpponentCardPlayed(Action<RawCardEvent> handler)
		{
			if(handler == null)
				return;
			GameEvents.OnOpponentPlay.Add(card => handler(CreateCardEvent(card, ParticipantSide.Opponent)));
		}

		public void RegisterPlayerCardDiscarded(Action<RawCardEvent> handler)
		{
			if(handler == null)
				return;
			GameEvents.OnPlayerHandDiscard.Add(card => handler(CreateCardEvent(card, ParticipantSide.Friendly)));
		}

		public void RegisterOpponentCardDiscarded(Action<RawCardEvent> handler)
		{
			if(handler == null)
				return;
			GameEvents.OnOpponentHandDiscard.Add(card => handler(CreateCardEvent(card, ParticipantSide.Opponent)));
		}

		public void RegisterAttackOccurred(Action<RawAttackEvent> handler)
		{
			if(handler == null)
				return;
			GameEvents.OnPlayerMinionAttack.Add(attack => handler(CreateAttackEvent(attack, ParticipantSide.Friendly)));
			GameEvents.OnOpponentMinionAttack.Add(attack => handler(CreateAttackEvent(attack, ParticipantSide.Opponent)));
		}

		public void RegisterHeroPowerUsed(Action<RawHeroPowerEvent> handler)
		{
			if(handler == null)
				return;
			GameEvents.OnPlayerHeroPower.Add(() => handler(new RawHeroPowerEvent(ParticipantSide.Friendly, DateTimeOffset.UtcNow)));
			GameEvents.OnOpponentHeroPower.Add(() => handler(new RawHeroPowerEvent(ParticipantSide.Opponent, DateTimeOffset.UtcNow)));
		}

		public void RegisterDeckSelected(Action<RawDeckSelectedEvent> handler)
		{
			if(handler == null)
				return;
			DeckManagerEvents.OnDeckSelected.Add(deck =>
			{
				var timestamp = DateTimeOffset.UtcNow;
				if(deck == null)
				{
					handler(new RawDeckSelectedEvent(null, null, null, timestamp));
					return;
				}

				var cardCount = deck.Cards?.Sum(card => card.Count) ?? 0;
				handler(new RawDeckSelectedEvent(deck.DeckId, deck.Name, cardCount, timestamp));
			});
		}

		public void RegisterUpdateTick(Action<RawUpdateTickEvent> handler)
		{
			if(handler == null)
				return;

			_updateHandlers += handler;
			if(!_updateTimer.IsEnabled)
				_updateTimer.Start();
		}

		public void RegisterOpponentMulliganSummary(Action<RawOpponentMulliganSummaryEvent> handler)
		{
			if(handler == null)
				return;

			_opponentMulliganHandlers += handler;
			if(!_updateTimer.IsEnabled)
				_updateTimer.Start();
		}

		public void RegisterOpponentCardCreated(Action<RawCardCreatedEvent> handler)
		{
			if(handler == null)
				return;

			_opponentCreatedHandlers += handler;
			if(!_updateTimer.IsEnabled)
				_updateTimer.Start();
		}

		public void Dispose()
		{
			if(_disposed)
				return;

			_disposed = true;
			_updateTimer.Tick -= OnUpdateTick;
			_updateTimer.Stop();
			_updateHandlers = null;
			_opponentMulliganHandlers = null;
			_opponentCreatedHandlers = null;
			_knownCreatedEntities.Clear();
			Core.Game.SecretsManager.OnSecretsChanged -= _ => _secretsChangedHandlers?.Invoke();
		}

		private void OnUpdateTick(object? sender, EventArgs e)
		{
			TryEmitOpponentMulliganSummary();
			TryEmitOpponentCreatedEvents();

			var handlers = _updateHandlers;
			if(handlers != null)
			{
				var tick = new RawUpdateTickEvent(DateTimeOffset.UtcNow);
				handlers.Invoke(tick);
			}

			if(_updateHandlers == null && _opponentMulliganHandlers == null && _updateTimer.IsEnabled)
				_updateTimer.Stop();
		}

		private void ResetOpponentMulliganState()
		{
			_opponentMulliganEmitted = false;
			_knownCreatedEntities.Clear();
		}

		private void TryEmitOpponentMulliganSummary()
		{
			if(_opponentMulliganHandlers == null || _opponentMulliganEmitted)
				return;

			var game = Core.Game;
			if(game == null || !game.IsRunning || !game.IsMulliganDone)
				return;

			var opponent = game.Opponent;
			if(opponent == null)
				return;

			if(!TryBuildMulliganSlots(opponent, out var slots))
				return;

			_opponentMulliganEmitted = true;
			var summary = new RawOpponentMulliganSummaryEvent(slots, DateTimeOffset.UtcNow, opponent.HasCoin);
			_opponentMulliganHandlers?.Invoke(summary);
		}

		private static bool TryBuildMulliganSlots(Player opponent, out IReadOnlyList<RawOpponentMulliganSlot> slots)
		{
			var startingHand = opponent.StartingHand;
			if(startingHand == null || startingHand.Count == 0)
			{
				slots = Array.Empty<RawOpponentMulliganSlot>();
				return true;
			}

			var results = new List<RawOpponentMulliganSlot>();

			foreach(var entity in startingHand)
			{
				if(entity == null)
					continue;

				var position = entity.GetTag(GameTag.ZONE_POSITION);
				if(position <= 0)
					continue;

				var wasKept = !entity.Info.Mulliganed;
				var isKnown = !entity.Info.Hidden && !string.IsNullOrWhiteSpace(entity.CardId);
				var cardId = isKnown ? entity.CardId : null;

				results.Add(new RawOpponentMulliganSlot(position - 1, wasKept, isKnown, cardId, entity.Id));
			}

			results.Sort((a, b) => a.Position.CompareTo(b.Position));
			slots = results.Count == 0
				? Array.Empty<RawOpponentMulliganSlot>()
				: results;
			return true;
		}

		private void TryEmitOpponentCreatedEvents()
		{
			var handlers = _opponentCreatedHandlers;
			if(handlers == null)
				return;

			var game = Core.Game;
			if(game == null || !game.IsRunning)
				return;

			var opponent = game.Opponent;
			if(opponent == null)
				return;

			var handEntities = opponent.Hand?.ToList() ?? new List<Entity>();

			foreach(var entity in handEntities)
			{
				if(entity == null)
					continue;

				if(!entity.Info.Created)
					continue;

				if(_knownCreatedEntities.Contains(entity.Id))
					continue;

				_knownCreatedEntities.Add(entity.Id);

				var position = entity.ZonePosition > 0 ? entity.ZonePosition - 1 : handEntities.FindIndex(e => e?.Id == entity.Id);
				if(position < 0)
					position = 0;

				var creatorEntityId = entity.GetTag(GameTag.CREATOR);
				string? creatorCardId = null;
				if(creatorEntityId > 0 && game.Entities.TryGetValue(creatorEntityId, out var creatorEntity))
					creatorCardId = creatorEntity.CardId ?? creatorEntity.Info.LatestCardId;

				var candidates = entity.Info.StoredCardIds?.Where(id => !string.IsNullOrWhiteSpace(id)).ToList() ?? new List<string>();
				if(candidates.Count == 0 && creatorEntityId > 0)
				{
					if(game.Entities.TryGetValue(creatorEntityId, out var sourceEntity))
					{
						var parentCandidates = sourceEntity.Info.StoredCardIds?.Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
						if(parentCandidates != null && parentCandidates.Count > 0)
							candidates = parentCandidates;
					}
				}
				var origin = candidates.Count > 0 ? CardOriginType.Discovered : CardOriginType.Created;

				var createdEvent = new RawCardCreatedEvent(
					ParticipantSide.Opponent,
					entity.CardId,
					entity.Id,
					creatorCardId,
					creatorEntityId > 0 ? creatorEntityId : (int?)null,
					origin,
					DateTimeOffset.UtcNow,
					position,
					candidates);

				handlers.Invoke(createdEvent);
			}
		}

		private static RawCardEvent CreateCardEvent(Card card, ParticipantSide controller)
		{
			return new RawCardEvent(controller, CreateCardSnapshot(card, controller), DateTimeOffset.UtcNow);
		}

		private static RawCardEvent CreateUnknownCardEvent(ParticipantSide controller)
		{
			return new RawCardEvent(controller, new CardSnapshot(controller, null, null, null, false), DateTimeOffset.UtcNow);
		}

		private static RawAttackEvent CreateAttackEvent(AttackInfo attack, ParticipantSide attackerSide)
		{
			var defenderSide = attackerSide == ParticipantSide.Friendly
				? ParticipantSide.Opponent
				: ParticipantSide.Friendly;
			return new RawAttackEvent(
				CreateCardSnapshot(attack.Attacker, attackerSide),
				CreateCardSnapshot(attack.Defender, defenderSide),
				DateTimeOffset.UtcNow);
		}

		private static CardSnapshot CreateCardSnapshot(Card card, ParticipantSide controller)
		{
			var localizedName = card.LocalizedName ?? card.Name;
			var cardId = card.Id;
			var isKnown = card.IsKnownCard && !string.IsNullOrWhiteSpace(cardId) && !string.Equals(cardId, "unknown", StringComparison.OrdinalIgnoreCase);
			return new CardSnapshot(controller, cardId, localizedName, null, isKnown);
		}

		public RawSecretsSnapshot CaptureOpponentSecretsSnapshot()
		{
			var timestamp = DateTimeOffset.UtcNow;

			var game = Core.Game;
			if(game == null || !game.IsRunning)
				return new RawSecretsSnapshot(Array.Empty<RawSecretGroup>(), timestamp);

			var secretsManager = game.SecretsManager;
			if(secretsManager == null)
				return new RawSecretsSnapshot(Array.Empty<RawSecretGroup>(), timestamp);

			var groups = new List<RawSecretGroup>();

			var opponentId = game.Opponent?.Id ?? -1;
			foreach(var secret in secretsManager.Secrets)
			{
				if(secret?.Entity == null)
					continue;
				// Track opponent secrets only
				if(opponentId != -1 && !secret.Entity.IsControlledBy(opponentId))
					continue;

				var entity = secret.Entity;
				var cardClassTag = entity.GetTag(GameTag.CLASS);
				string? cardClass = null;
				if(Enum.IsDefined(typeof(CardClass), cardClassTag))
					cardClass = ((CardClass)cardClassTag).ToString();

				var remaining = secret.Excluded
					.Where(kvp => !kvp.Value)
					.SelectMany(kvp => kvp.Key.Ids.Where(id => !string.IsNullOrWhiteSpace(id)).Take(1))
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.ToList();

				var revealedCardId = entity.HasCardId ? entity.CardId : null;

				groups.Add(new RawSecretGroup(cardClass, entity.Id, revealedCardId, remaining));
			}

			return new RawSecretsSnapshot(groups, timestamp);
		}

		public RawHandOddsSnapshot CaptureOpponentHandOddsSnapshot(ParticipantSide activeSide)
		{
			var timestamp = DateTimeOffset.UtcNow;

			var game = Core.Game;
			if(game == null || !game.IsRunning)
				return new RawHandOddsSnapshot(0, 0, false, null, null, timestamp, activeSide);

			var opponent = game.Opponent;
			if(opponent == null)
				return new RawHandOddsSnapshot(0, 0, false, null, null, timestamp, activeSide);

			var handCount = opponent.HandCount;
			var deckCount = opponent.DeckCount;
			var hasCoin = opponent.HasCoin;
			var handWithoutCoin = hasCoin ? Math.Max(handCount - 1, 0) : handCount;
			var draws = handWithoutCoin + 1;
			var population = deckCount + handWithoutCoin;

			double? singleCopy = null;
			double? doubleCopy = null;

			if(population > 0 && draws > 0 && population >= draws)
			{
				singleCopy = Helper.DrawProbability(1, population, draws);
				if(population >= 2)
					doubleCopy = Helper.DrawProbability(2, population, draws);
			}

			return new RawHandOddsSnapshot(
				handCount,
				deckCount,
				hasCoin,
				singleCopy,
				doubleCopy,
				timestamp,
				activeSide);
		}

		public void RegisterSecretsChanged(Action handler)
		{
			if(handler == null)
				return;
			_secretsChangedHandlers += handler;
		}
	}
}









