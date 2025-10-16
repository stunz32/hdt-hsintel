using System;
using System.Collections.Generic;
using System.Diagnostics;
using HSIntel.Core.Interfaces;
using HSIntel.Core.Models;
using HSIntel.Core.Models.Diagnostics;
using HSIntel.Core.Models.Events;
using HSIntel.Core.Models.Snapshots;

namespace HSIntel.Core.Services
{
    /// <summary>
    /// Hydrates immutable HSIntel state models from HDT game data whenever semantic events occur.
    /// </summary>
    public sealed class StateBuilder : IDisposable
    {
        private readonly HDTEventBinder _eventBinder;
        private readonly IGameStateSource _stateSource;
        private readonly IStateValidator _validator;
        private readonly StateUpdateMetrics _metrics = new StateUpdateMetrics();
        private readonly object _syncRoot = new object();

        private bool _initialized;
        private bool _disposed;
        private DataQuality _binderDataQuality = DataQuality.Normal;
        private DataQuality _snapshotDataQuality = DataQuality.Normal;

        private BoardState _boardState = BoardState.Empty;
        private HandState _friendlyHand = HandState.CreateEmpty(ParticipantSide.Friendly);
        private HandState _opponentHand = HandState.CreateEmpty(ParticipantSide.Opponent);
        private ManaState _friendlyMana = ManaState.Unknown(ParticipantSide.Friendly);
        private ManaState _opponentMana = ManaState.Unknown(ParticipantSide.Opponent);
        private GameContext _gameContext = GameContext.Empty;

        public StateBuilder(HDTEventBinder eventBinder, IGameStateSource stateSource, IStateValidator? stateValidator = null)
        {
            _eventBinder = eventBinder ?? throw new ArgumentNullException(nameof(eventBinder));
            _stateSource = stateSource ?? throw new ArgumentNullException(nameof(stateSource));
            _validator = stateValidator ?? new StateValidator();
        }

        public event EventHandler<GameContextUpdatedEventArgs>? GameContextUpdated;

        public event EventHandler<DataQualityChangedEventArgs>? DataQualityChanged;

        public event EventHandler<StateValidationEventArgs>? StateValidated;

        public event EventHandler<StateMetricsEventArgs>? StateMetricsUpdated;

        public BoardState CurrentBoardState
        {
            get
            {
                lock(_syncRoot)
                    return _boardState;
            }
        }

        public HandState CurrentFriendlyHand
        {
            get
            {
                lock(_syncRoot)
                    return _friendlyHand;
            }
        }

        public HandState CurrentOpponentHand
        {
            get
            {
                lock(_syncRoot)
                    return _opponentHand;
            }
        }

        public GameContext CurrentGameContext
        {
            get
            {
                lock(_syncRoot)
                    return _gameContext;
            }
        }

        public StateUpdateMetricsSnapshot CurrentMetrics
        {
            get
            {
                lock(_syncRoot)
                    return _metrics.CreateSnapshot();
            }
        }

        public void Initialize()
        {
            if(_initialized)
                return;

            _eventBinder.GameStarted += HandleGameStarted;
            _eventBinder.GameEnded += HandleGameEnded;
            _eventBinder.TurnStarted += HandleTurnStarted;
            _eventBinder.BoardStateChanged += HandleBoardStateChanged;
            _eventBinder.UpdateTick += HandleUpdateTick;
            _eventBinder.DataQualityChanged += HandleBinderDataQualityChanged;

            _initialized = true;
        }

        public void Dispose()
        {
            if(_disposed)
                return;

            _disposed = true;

            _eventBinder.GameStarted -= HandleGameStarted;
            _eventBinder.GameEnded -= HandleGameEnded;
            _eventBinder.TurnStarted -= HandleTurnStarted;
            _eventBinder.BoardStateChanged -= HandleBoardStateChanged;
            _eventBinder.UpdateTick -= HandleUpdateTick;
            _eventBinder.DataQualityChanged -= HandleBinderDataQualityChanged;
        }

        public GameContext CaptureSnapshot()
        {
            lock(_syncRoot)
                return _gameContext;
        }

        private void HandleGameStarted(object? sender, GameLifecycleEventArgs e)
        {
            ResetState();
            var args = RebuildState(
                StateUpdateKind.GameStarted,
                null,
                e.Timestamp,
                out var validationArgs,
                out var metricsArgs);
            DispatchUpdates(args, validationArgs, metricsArgs);
        }

        private void HandleGameEnded(object? sender, GameLifecycleEventArgs e)
        {
            var args = RebuildState(
                StateUpdateKind.GameEnded,
                null,
                e.Timestamp,
                out var validationArgs,
                out var metricsArgs);
            DispatchUpdates(args, validationArgs, metricsArgs);
        }

        private void HandleTurnStarted(object? sender, TurnStartedEventArgs e)
        {
            var args = RebuildState(
                StateUpdateKind.TurnStarted,
                null,
                e.Timestamp,
                out var validationArgs,
                out var metricsArgs);
            DispatchUpdates(args, validationArgs, metricsArgs);
        }

        private void HandleBoardStateChanged(object? sender, BoardStateChangedEventArgs e)
        {
            var args = RebuildState(
                MapBoardReason(e.Reason),
                e.Reason,
                e.Timestamp,
                out var validationArgs,
                out var metricsArgs);
            DispatchUpdates(args, validationArgs, metricsArgs);
        }

        private void HandleUpdateTick(object? sender, UpdateTickEventArgs e)
        {
            var args = RebuildState(
                StateUpdateKind.UpdateTick,
                null,
                e.Timestamp,
                out var validationArgs,
                out var metricsArgs);
            DispatchUpdates(args, validationArgs, metricsArgs);
        }

        private void HandleBinderDataQualityChanged(object? sender, DataQualityChangedEventArgs e)
        {
            _binderDataQuality = e.DataQuality;

            var args = ApplyCombinedDataQuality(
                e.Timestamp,
                StateUpdateKind.DataQualityChanged,
                out var validationArgs,
                out var metricsArgs);
            DispatchUpdates(args, validationArgs, metricsArgs);

            DataQualityChanged?.Invoke(this, e);
        }

        private void ResetState()
        {
            lock(_syncRoot)
            {
                _snapshotDataQuality = DataQuality.Normal;
                _boardState = BoardState.Empty;
                _friendlyHand = HandState.CreateEmpty(ParticipantSide.Friendly);
                _opponentHand = HandState.CreateEmpty(ParticipantSide.Opponent);
                _friendlyMana = ManaState.Unknown(ParticipantSide.Friendly);
                _opponentMana = ManaState.Unknown(ParticipantSide.Opponent);
                _gameContext = GameContext.Empty;
            }
        }

        private GameContextUpdatedEventArgs? RebuildState(
            StateUpdateKind updateKind,
            BoardStateChangeReason? boardChangeReason,
            DateTimeOffset timestamp,
            out StateValidationEventArgs? validationArgs,
            out StateMetricsEventArgs? metricsArgs)
        {
            validationArgs = null;
            metricsArgs = null;
            GameStateSnapshot snapshot;

            try
            {
                snapshot = _stateSource.CaptureSnapshot();
            }
            catch
            {
                // Snapshot failures degrade quality but preserve prior known state.
                return ApplyCombinedDataQuality(
                    timestamp,
                    StateUpdateKind.DataQualityChanged,
                    out validationArgs,
                    out metricsArgs);
            }

            lock(_syncRoot)
            {
                _snapshotDataQuality = snapshot.DataQuality;
                var combinedQuality = CombineQuality(_binderDataQuality, _snapshotDataQuality);

                _boardState = BuildBoardState(snapshot, combinedQuality, timestamp);
                _friendlyHand = BuildHandState(
                    snapshot.FriendlyHand,
                    ParticipantSide.Friendly,
                    snapshot.FriendlyHandCount,
                    combinedQuality,
                    timestamp);
                _opponentHand = BuildHandState(
                    snapshot.OpponentHand,
                    ParticipantSide.Opponent,
                    snapshot.OpponentHandCount,
                    combinedQuality,
                    timestamp);
                _friendlyMana = ToManaState(snapshot.FriendlyMana);
                _opponentMana = ToManaState(snapshot.OpponentMana);

                _gameContext = new GameContext(
                    _boardState,
                    _friendlyHand,
                    _opponentHand,
                    _friendlyMana,
                    _opponentMana,
                    snapshot.TurnNumber,
                    snapshot.ActiveSide,
                    combinedQuality,
                    timestamp,
                    snapshot.GameId);

                _metrics.Record(timestamp);
                metricsArgs = new StateMetricsEventArgs(_metrics.CreateSnapshot());

                var validationResult = _validator.Validate(_gameContext, timestamp);
                validationArgs = new StateValidationEventArgs(validationResult);

                return new GameContextUpdatedEventArgs(
                    _gameContext,
                    _boardState,
                    _friendlyHand,
                    _opponentHand,
                    _friendlyMana,
                    _opponentMana,
                    updateKind,
                    boardChangeReason,
                    timestamp,
                    combinedQuality);
            }
        }

        private GameContextUpdatedEventArgs? ApplyCombinedDataQuality(
            DateTimeOffset timestamp,
            StateUpdateKind updateKind,
            out StateValidationEventArgs? validationArgs,
            out StateMetricsEventArgs? metricsArgs)
        {
            validationArgs = null;
            metricsArgs = null;
            lock(_syncRoot)
            {
                var combined = CombineQuality(_binderDataQuality, _snapshotDataQuality);

                _boardState = _boardState.With(dataQuality: combined, lastUpdated: timestamp);
                _friendlyHand = _friendlyHand.With(dataQuality: combined, lastUpdated: timestamp);
                _opponentHand = _opponentHand.With(dataQuality: combined, lastUpdated: timestamp);
                _gameContext = _gameContext.With(
                    board: _boardState,
                    friendlyHand: _friendlyHand,
                    opponentHand: _opponentHand,
                    friendlyMana: _friendlyMana,
                    opponentMana: _opponentMana,
                    dataQuality: combined,
                    lastUpdated: timestamp);

                var validationResult = _validator.Validate(_gameContext, timestamp);
                validationArgs = new StateValidationEventArgs(validationResult);

                return new GameContextUpdatedEventArgs(
                    _gameContext,
                    _boardState,
                    _friendlyHand,
                    _opponentHand,
                    _friendlyMana,
                    _opponentMana,
                    updateKind,
                    null,
                    timestamp,
                    combined);
            }
        }

        private void DispatchUpdates(
            GameContextUpdatedEventArgs? contextArgs,
            StateValidationEventArgs? validationArgs,
            StateMetricsEventArgs? metricsArgs)
        {
            if(validationArgs != null && !validationArgs.IsValid)
            {
                foreach(var issue in validationArgs.Result.Issues)
                {
                    var level = issue.Severity == ValidationSeverity.Error ? "ERROR" : "WARN";
                    Trace.WriteLine($"[HSIntel][StateValidation][{level}] {issue.Code}: {issue.Message}");
                }
            }

            if(contextArgs != null)
                GameContextUpdated?.Invoke(this, contextArgs);

            if(validationArgs != null)
                StateValidated?.Invoke(this, validationArgs);

            if(metricsArgs != null)
                StateMetricsUpdated?.Invoke(this, metricsArgs);
        }

        private static BoardState BuildBoardState(
            GameStateSnapshot snapshot,
            DataQuality dataQuality,
            DateTimeOffset timestamp)
        {
            var friendlyHero = ToHeroState(snapshot.FriendlyHero);
            var opponentHero = ToHeroState(snapshot.OpponentHero);
            var friendlyMinions = MapMinions(snapshot.FriendlyMinions);
            var opponentMinions = MapMinions(snapshot.OpponentMinions);
            var secrets = MapSecrets(snapshot.Secrets);

            return new BoardState(
                friendlyHero,
                opponentHero,
                friendlyMinions,
                opponentMinions,
                secrets,
                dataQuality,
                timestamp);
        }

        private static HandState BuildHandState(
            IReadOnlyList<HandCardDescriptor> cards,
            ParticipantSide owner,
            int totalCount,
            DataQuality dataQuality,
            DateTimeOffset timestamp)
        {
            var handCards = MapHandCards(cards);
            return HandState.Create(owner, handCards, totalCount, dataQuality, timestamp);
        }

        private static IReadOnlyList<MinionState> MapMinions(IReadOnlyList<MinionDescriptor> descriptors)
        {
            if(descriptors.Count == 0)
                return Array.Empty<MinionState>();

            var minions = new List<MinionState>(descriptors.Count);

            foreach(var descriptor in descriptors)
            {
                minions.Add(
                    new MinionState(
                        descriptor.Controller,
                        descriptor.EntityId,
                        descriptor.CardId,
                        descriptor.CardName,
                        descriptor.Attack,
                        descriptor.Health,
                        descriptor.BoardPosition,
                        descriptor.Keywords,
                        descriptor.HasDivineShield,
                        descriptor.HasTaunt,
                        descriptor.IsStealthed,
                        descriptor.IsFrozen,
                        descriptor.IsDormant));
            }

            return minions;
        }

        private static IReadOnlyList<SecretState> MapSecrets(IReadOnlyList<SecretDescriptor> descriptors)
        {
            if(descriptors.Count == 0)
                return Array.Empty<SecretState>();

            var secrets = new List<SecretState>(descriptors.Count);

            foreach(var descriptor in descriptors)
            {
                secrets.Add(
                    new SecretState(
                        descriptor.Controller,
                        descriptor.EntityId,
                        descriptor.CardId,
                        descriptor.CardName,
                        descriptor.TurnsInPlay,
                        descriptor.WasCreated));
            }

            return secrets;
        }

        private static IReadOnlyList<HandCardState> MapHandCards(IReadOnlyList<HandCardDescriptor> descriptors)
        {
            if(descriptors.Count == 0)
                return Array.Empty<HandCardState>();

            var cards = new List<HandCardState>(descriptors.Count);

            foreach(var descriptor in descriptors)
            {
                cards.Add(
                    new HandCardState(
                        descriptor.Controller,
                        descriptor.EntityId,
                        descriptor.CardId,
                        descriptor.CardName,
                        descriptor.BaseCost,
                        descriptor.EffectiveCost,
                        descriptor.IsKnown,
                        descriptor.IsPlayable,
                        descriptor.ZonePosition,
                        MapCreatedBy(descriptor.CreatedBy),
                        descriptor.WasGeneratedThisTurn));
            }

            return cards;
        }

        private static IReadOnlyList<CreatedByTag> MapCreatedBy(IReadOnlyList<CreatedByDescriptor> descriptors)
        {
            if(descriptors.Count == 0)
                return Array.Empty<CreatedByTag>();

            var tags = new List<CreatedByTag>(descriptors.Count);

            foreach(var descriptor in descriptors)
            {
                tags.Add(new CreatedByTag(descriptor.SourceCardId, descriptor.SourceCardName, descriptor.SourceEntityId));
            }

            return tags;
        }

        private static HeroState ToHeroState(HeroDescriptor descriptor)
        {
            return new HeroState(
                descriptor.Side,
                descriptor.Health,
                descriptor.Armor,
                descriptor.Weapon != null ? ToWeaponState(descriptor.Weapon) : null,
                descriptor.IsImmune,
                descriptor.IsFrozen);
        }

        private static WeaponState ToWeaponState(WeaponDescriptor descriptor)
        {
            return new WeaponState(
                descriptor.EntityId,
                descriptor.CardId,
                descriptor.CardName,
                descriptor.Attack,
                descriptor.Durability,
                descriptor.IsPoisonous,
                descriptor.HasWindfury);
        }

        private static ManaState ToManaState(ManaDescriptor descriptor)
        {
            return new ManaState(
                descriptor.Owner,
                descriptor.Total,
                descriptor.Available,
                descriptor.Overloaded,
                descriptor.Locked,
                descriptor.Temporary);
        }

        private static DataQuality CombineQuality(DataQuality primary, DataQuality secondary) =>
            primary == DataQuality.Degraded || secondary == DataQuality.Degraded
                ? DataQuality.Degraded
                : DataQuality.Normal;

        private static StateUpdateKind MapBoardReason(BoardStateChangeReason reason)
        {
            return reason switch
            {
                BoardStateChangeReason.CardDrawn => StateUpdateKind.CardDrawn,
                BoardStateChangeReason.CardPlayed => StateUpdateKind.CardPlayed,
                BoardStateChangeReason.CardDiscarded => StateUpdateKind.CardDiscarded,
                BoardStateChangeReason.Attack => StateUpdateKind.Attack,
                BoardStateChangeReason.HeroPower => StateUpdateKind.HeroPower,
                BoardStateChangeReason.TurnStarted => StateUpdateKind.TurnStarted,
                BoardStateChangeReason.DeckSelected => StateUpdateKind.DeckSelected,
                BoardStateChangeReason.GameStarted => StateUpdateKind.GameStarted,
                BoardStateChangeReason.GameEnded => StateUpdateKind.GameEnded,
                _ => StateUpdateKind.BoardChanged
            };
        }
    }
}
