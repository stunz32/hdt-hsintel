using System;
using System.Collections.Generic;
using HSIntel.Core.Models;
using HSIntel.Core.Models.Diagnostics;

namespace HSIntel.Core.Models.Events
{
    public abstract class CardEventArgs : EventArgs
    {
        protected CardEventArgs(CardSnapshot card, DateTimeOffset timestamp)
        {
            Card = card;
            Timestamp = timestamp;
        }

        public CardSnapshot Card { get; }

        public DateTimeOffset Timestamp { get; }
    }

    public sealed class CardDrawnEventArgs : CardEventArgs
    {
        public CardDrawnEventArgs(CardSnapshot card, DateTimeOffset timestamp)
            : base(card, timestamp)
        {
        }
    }

    public sealed class CardPlayedEventArgs : CardEventArgs
    {
        public CardPlayedEventArgs(CardSnapshot card, DateTimeOffset timestamp)
            : base(card, timestamp)
        {
        }
    }

    public sealed class CardDiscardedEventArgs : CardEventArgs
    {
        public CardDiscardedEventArgs(CardSnapshot card, DateTimeOffset timestamp)
            : base(card, timestamp)
        {
        }
    }

    public sealed class AttackEventArgs : EventArgs
    {
        public AttackEventArgs(CardSnapshot attacker, CardSnapshot defender, DateTimeOffset timestamp)
        {
            Attacker = attacker;
            Defender = defender;
            Timestamp = timestamp;
        }

        public CardSnapshot Attacker { get; }

        public CardSnapshot Defender { get; }

        public DateTimeOffset Timestamp { get; }
    }

    public sealed class HeroPowerUsedEventArgs : EventArgs
    {
        public HeroPowerUsedEventArgs(ParticipantSide controller, DateTimeOffset timestamp)
        {
            Controller = controller;
            Timestamp = timestamp;
        }

        public ParticipantSide Controller { get; }

        public DateTimeOffset Timestamp { get; }
    }

    public sealed class DeckSelectedEventArgs : EventArgs
    {
        public DeckSelectedEventArgs(Guid? deckId, string? deckName, int? cardCount, DateTimeOffset timestamp)
        {
            DeckId = deckId;
            DeckName = deckName;
            CardCount = cardCount;
            Timestamp = timestamp;
        }

        public Guid? DeckId { get; }

        public string? DeckName { get; }

        public int? CardCount { get; }

        public DateTimeOffset Timestamp { get; }
    }

    public sealed class TurnStartedEventArgs : EventArgs
    {
        public TurnStartedEventArgs(ParticipantSide activeSide, int turnNumber, DateTimeOffset timestamp)
        {
            ActiveSide = activeSide;
            TurnNumber = turnNumber;
            Timestamp = timestamp;
        }

        public ParticipantSide ActiveSide { get; }

        public int TurnNumber { get; }

        public DateTimeOffset Timestamp { get; }
    }

    public sealed class BoardStateChangedEventArgs : EventArgs
    {
        public BoardStateChangedEventArgs(
            BoardStateChangeReason reason,
            DateTimeOffset timestamp,
            CardSnapshot? subjectCard,
            ParticipantSide? participant)
        {
            Reason = reason;
            Timestamp = timestamp;
            SubjectCard = subjectCard;
            Participant = participant;
        }

        public BoardStateChangeReason Reason { get; }

        public DateTimeOffset Timestamp { get; }

        public CardSnapshot? SubjectCard { get; }

        public ParticipantSide? Participant { get; }
    }

    public sealed class GameLifecycleEventArgs : EventArgs
    {
        public GameLifecycleEventArgs(DateTimeOffset timestamp)
        {
            Timestamp = timestamp;
        }

        public DateTimeOffset Timestamp { get; }
    }

    public sealed class UpdateTickEventArgs : EventArgs
    {
        public UpdateTickEventArgs(DateTimeOffset timestamp, TimeSpan elapsed, DataQuality dataQuality, bool isLate)
        {
            Timestamp = timestamp;
            Elapsed = elapsed;
            DataQuality = dataQuality;
            IsLate = isLate;
        }

        public DateTimeOffset Timestamp { get; }

        public TimeSpan Elapsed { get; }

        public DataQuality DataQuality { get; }

        public bool IsLate { get; }
    }

    public sealed class DataQualityChangedEventArgs : EventArgs
    {
        public DataQualityChangedEventArgs(DataQuality dataQuality, string reason, DateTimeOffset timestamp)
        {
            DataQuality = dataQuality;
            Reason = reason;
            Timestamp = timestamp;
        }

        public DataQuality DataQuality { get; }

        public string Reason { get; }

        public DateTimeOffset Timestamp { get; }
    }

    public enum StateUpdateKind
    {
        Unknown = 0,
        GameStarted,
        GameEnded,
        TurnStarted,
        CardDrawn,
        CardPlayed,
        CardDiscarded,
        Attack,
        HeroPower,
        DeckSelected,
        UpdateTick,
        BoardChanged,
        DataQualityChanged
    }

    public sealed class GameContextUpdatedEventArgs : EventArgs
    {
        public GameContextUpdatedEventArgs(
            GameContext context,
            BoardState board,
            HandState friendlyHand,
            HandState opponentHand,
            ManaState friendlyMana,
            ManaState opponentMana,
            StateUpdateKind updateKind,
            BoardStateChangeReason? boardChangeReason,
            DateTimeOffset timestamp,
            DataQuality dataQuality)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            Board = board ?? throw new ArgumentNullException(nameof(board));
            FriendlyHand = friendlyHand ?? throw new ArgumentNullException(nameof(friendlyHand));
            OpponentHand = opponentHand ?? throw new ArgumentNullException(nameof(opponentHand));
            FriendlyMana = friendlyMana ?? throw new ArgumentNullException(nameof(friendlyMana));
            OpponentMana = opponentMana ?? throw new ArgumentNullException(nameof(opponentMana));
            UpdateKind = updateKind;
            BoardChangeReason = boardChangeReason;
            Timestamp = timestamp;
            DataQuality = dataQuality;
        }

        public GameContext Context { get; }

        public BoardState Board { get; }

        public HandState FriendlyHand { get; }

        public HandState OpponentHand { get; }

        public ManaState FriendlyMana { get; }

        public ManaState OpponentMana { get; }

        public StateUpdateKind UpdateKind { get; }

        public BoardStateChangeReason? BoardChangeReason { get; }

        public DateTimeOffset Timestamp { get; }

        public DataQuality DataQuality { get; }

        public ParticipantSide? ActiveSide => Context.ActiveSide;

        public int? TurnNumber => Context.TurnNumber;

        public string? GameId => Context.GameId;
    }

    public sealed class StateValidationEventArgs : EventArgs
    {
        public StateValidationEventArgs(StateValidationResult result)
        {
            Result = result ?? throw new ArgumentNullException(nameof(result));
        }

        public StateValidationResult Result { get; }

        public bool IsValid => Result.IsValid;
    }

    public sealed class StateMetricsEventArgs : EventArgs
    {
        public StateMetricsEventArgs(StateUpdateMetricsSnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        public StateUpdateMetricsSnapshot Snapshot { get; }
    }

    public sealed class OpponentMulliganEventArgs : EventArgs
    {
        public OpponentMulliganEventArgs(
            IReadOnlyList<OpponentMulliganSlotInfo> slots,
            bool coinKept,
            DateTimeOffset timestamp)
        {
            Slots = slots ?? throw new ArgumentNullException(nameof(slots));
            CoinKept = coinKept;
            Timestamp = timestamp;
        }

        public IReadOnlyList<OpponentMulliganSlotInfo> Slots { get; }

        public bool CoinKept { get; }

        public DateTimeOffset Timestamp { get; }
    }

    public sealed class OpponentMulliganSlotInfo
    {
        public OpponentMulliganSlotInfo(
            int position,
            bool wasKept,
            bool isKnown,
            string? cardId,
            int? entityId)
        {
            Position = position;
            WasKept = wasKept;
            IsKnown = isKnown;
            CardId = cardId;
            EntityId = entityId;
        }

        public int Position { get; }

        public bool WasKept { get; }

        public bool IsKnown { get; }

        public string? CardId { get; }

        public int? EntityId { get; }
    }

    public sealed class OpponentModelUpdatedEventArgs : EventArgs
    {
        public OpponentModelUpdatedEventArgs(OpponentModel model, DateTimeOffset timestamp)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
            Timestamp = timestamp;
        }

        public OpponentModel Model { get; }

        public DateTimeOffset Timestamp { get; }
    }

    public sealed class OpponentCardCreatedEventArgs : EventArgs
    {
        public OpponentCardCreatedEventArgs(
            string? cardId,
            int? entityId,
            string? createdByCardId,
            int? createdByEntityId,
            CardOriginType origin,
            IReadOnlyList<string> candidateCardIds,
            int position,
            DateTimeOffset timestamp)
        {
            CardId = cardId;
            EntityId = entityId;
            CreatedByCardId = createdByCardId;
            CreatedByEntityId = createdByEntityId;
            Origin = origin;
            CandidateCardIds = candidateCardIds ?? Array.Empty<string>();
            Position = position;
            Timestamp = timestamp;
        }

        public string? CardId { get; }

        public int? EntityId { get; }

        public string? CreatedByCardId { get; }

        public int? CreatedByEntityId { get; }

        public CardOriginType Origin { get; }

        public IReadOnlyList<string> CandidateCardIds { get; }

        public int Position { get; }

        public DateTimeOffset Timestamp { get; }
    }

    public sealed class OpponentSecretsChangedEventArgs : EventArgs
    {
        public OpponentSecretsChangedEventArgs(
            IReadOnlyList<SecretCandidateGroup> groups,
            DateTimeOffset timestamp,
            string? cause)
        {
            Groups = groups ?? Array.Empty<SecretCandidateGroup>();
            Timestamp = timestamp;
            Cause = cause;
        }

        public IReadOnlyList<SecretCandidateGroup> Groups { get; }

        public DateTimeOffset Timestamp { get; }

        public string? Cause { get; }
    }

    public sealed class OpponentHandOddsEventArgs : EventArgs
    {
        public OpponentHandOddsEventArgs(
            double? singleCopyProbability,
            double? doubleCopyProbability,
            int handCount,
            int deckCount,
            bool hasCoin,
            ParticipantSide activeSide,
            DateTimeOffset timestamp)
        {
            SingleCopyProbability = singleCopyProbability;
            DoubleCopyProbability = doubleCopyProbability;
            HandCount = handCount;
            DeckCount = deckCount;
            HasCoin = hasCoin;
            ActiveSide = activeSide;
            Timestamp = timestamp;
        }

        public double? SingleCopyProbability { get; }

        public double? DoubleCopyProbability { get; }

        public int HandCount { get; }

        public int DeckCount { get; }

        public bool HasCoin { get; }

        public ParticipantSide ActiveSide { get; }

        public DateTimeOffset Timestamp { get; }
    }
}
