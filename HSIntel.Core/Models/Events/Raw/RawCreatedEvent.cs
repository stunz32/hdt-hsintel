using System;
using System.Collections.Generic;
using HSIntel.Core.Models;

namespace HSIntel.Core.Models.Events.Raw
{
    public sealed class RawCardCreatedEvent
    {
        public RawCardCreatedEvent(
            ParticipantSide controller,
            string? cardId,
            int? entityId,
            string? createdByCardId,
            int? createdByEntityId,
            CardOriginType origin,
            DateTimeOffset timestamp,
            int position,
            IReadOnlyList<string> candidateCardIds)
        {
            Controller = controller;
            CardId = cardId;
            EntityId = entityId;
            CreatedByCardId = createdByCardId;
            CreatedByEntityId = createdByEntityId;
            Origin = origin;
            Timestamp = timestamp;
            Position = position;
            CandidateCardIds = candidateCardIds;
        }

        public ParticipantSide Controller { get; }

        public string? CardId { get; }

        public int? EntityId { get; }

        public string? CreatedByCardId { get; }

        public int? CreatedByEntityId { get; }

        public CardOriginType Origin { get; }

        public DateTimeOffset Timestamp { get; }

        public int Position { get; }

        public IReadOnlyList<string> CandidateCardIds { get; }
    }
}
