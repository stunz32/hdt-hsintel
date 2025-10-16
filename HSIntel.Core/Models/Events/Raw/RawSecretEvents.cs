using System;
using System.Collections.Generic;

namespace HSIntel.Core.Models.Events.Raw
{
    public sealed class RawSecretGroup
    {
        public RawSecretGroup(
            string? cardClass,
            int? entityId,
            string? revealedCardId,
            IReadOnlyList<string> candidateCardIds)
        {
            CardClass = cardClass;
            EntityId = entityId;
            RevealedCardId = revealedCardId;
            CandidateCardIds = candidateCardIds ?? Array.Empty<string>();
        }

        public string? CardClass { get; }

        public int? EntityId { get; }

        public string? RevealedCardId { get; }

        public IReadOnlyList<string> CandidateCardIds { get; }
    }

    public sealed class RawSecretsSnapshot
    {
        public RawSecretsSnapshot(IReadOnlyList<RawSecretGroup> groups, DateTimeOffset timestamp)
        {
            Groups = groups ?? Array.Empty<RawSecretGroup>();
            Timestamp = timestamp;
        }

        public IReadOnlyList<RawSecretGroup> Groups { get; }

        public DateTimeOffset Timestamp { get; }

        public bool IsEmpty => Groups.Count == 0;
    }
}
