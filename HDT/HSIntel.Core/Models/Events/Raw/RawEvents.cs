using System;
using System.Collections.Generic;

namespace HSIntel.Core.Models.Events.Raw
{
    using HSIntel.Core.Models.Events;

    public sealed class RawCardEvent
    {
        public RawCardEvent(ParticipantSide controller, CardSnapshot card, DateTimeOffset timestamp)
        {
            Controller = controller;
            Card = card;
            Timestamp = timestamp;
        }

        public ParticipantSide Controller { get; }

        public CardSnapshot Card { get; }

        public DateTimeOffset Timestamp { get; }
    }

    public sealed class RawAttackEvent
    {
        public RawAttackEvent(CardSnapshot attacker, CardSnapshot defender, DateTimeOffset timestamp, int? damage = null)
        {
            Attacker = attacker;
            Defender = defender;
            Timestamp = timestamp;
            Damage = damage;
        }

        public CardSnapshot Attacker { get; }

        public CardSnapshot Defender { get; }

        public DateTimeOffset Timestamp { get; }

        public int? Damage { get; }
    }

    public sealed class RawHeroPowerEvent
    {
        public RawHeroPowerEvent(ParticipantSide controller, DateTimeOffset timestamp)
        {
            Controller = controller;
            Timestamp = timestamp;
        }

        public ParticipantSide Controller { get; }

        public DateTimeOffset Timestamp { get; }
    }

    public sealed class RawDeckSelectedEvent
    {
        public RawDeckSelectedEvent(Guid? deckId, string? deckName, int? cardCount, DateTimeOffset timestamp)
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

    public sealed class RawTurnStartEvent
    {
        public RawTurnStartEvent(ParticipantSide activeSide, int turnNumber, DateTimeOffset timestamp)
        {
            ActiveSide = activeSide;
            TurnNumber = turnNumber;
            Timestamp = timestamp;
        }

        public ParticipantSide ActiveSide { get; }

        public int TurnNumber { get; }

        public DateTimeOffset Timestamp { get; }
    }

    public sealed class RawUpdateTickEvent
    {
        public RawUpdateTickEvent(DateTimeOffset timestamp)
        {
            Timestamp = timestamp;
        }

        public DateTimeOffset Timestamp { get; }
    }

    public sealed class RawOpponentMulliganSummaryEvent
    {
        public RawOpponentMulliganSummaryEvent(
            IReadOnlyList<RawOpponentMulliganSlot> slots,
            DateTimeOffset timestamp,
            bool coinKept)
        {
            Slots = slots;
            Timestamp = timestamp;
            CoinKept = coinKept;
        }

        public IReadOnlyList<RawOpponentMulliganSlot> Slots { get; }

        public DateTimeOffset Timestamp { get; }

        /// <summary>
        /// Indicates whether the coin (if present) remained after mulligan.
        /// </summary>
        public bool CoinKept { get; }
    }

    public sealed class RawOpponentMulliganSlot
    {
        public RawOpponentMulliganSlot(int position, bool wasKept, bool isKnown, string? cardId, int? entityId)
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
}
