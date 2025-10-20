using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using HSIntel.Core.Models.Events;

namespace HSIntel.Core.Models
{
    /// <summary>
    /// Aggregated inference state for the opponent's resources.
    /// </summary>
    public sealed class OpponentModel
    {
        public static OpponentModel Empty { get; } = new OpponentModel(
            Array.Empty<OpponentHandSlot>(),
            Array.Empty<SecretCandidateGroup>(),
            DataQuality.Normal,
            null);

        public OpponentModel(
            IEnumerable<OpponentHandSlot> handSlots,
            IEnumerable<SecretCandidateGroup> secretCandidates,
            DataQuality dataQuality,
            DateTimeOffset? lastUpdated)
        {
            HandSlots = ToReadOnlyList(handSlots);
            SecretCandidates = ToReadOnlyList(secretCandidates);
            DataQuality = dataQuality;
            LastUpdated = lastUpdated;
        }

        /// <summary>
        /// Current view of the opponent hand slots (left-to-right, 0-based).
        /// </summary>
        public IReadOnlyList<OpponentHandSlot> HandSlots { get; }

        /// <summary>
        /// Secret candidates grouped by governing class.
        /// </summary>
        public IReadOnlyList<SecretCandidateGroup> SecretCandidates { get; }

        public DataQuality DataQuality { get; }

        public DateTimeOffset? LastUpdated { get; }

        public OpponentModel With(
            IEnumerable<OpponentHandSlot>? handSlots = null,
            IEnumerable<SecretCandidateGroup>? secretCandidates = null,
            DataQuality? dataQuality = null,
            DateTimeOffset? lastUpdated = null)
        {
            return new OpponentModel(
                handSlots ?? HandSlots,
                secretCandidates ?? SecretCandidates,
                dataQuality ?? DataQuality,
                lastUpdated ?? LastUpdated);
        }

        private static IReadOnlyList<T> ToReadOnlyList<T>(IEnumerable<T>? source)
        {
            if(source == null)
                return Array.Empty<T>();

            if(source is IReadOnlyList<T> readOnly)
                return readOnly;

            var list = new List<T>();

            foreach(var item in source)
                list.Add(item);

            return new ReadOnlyCollection<T>(list);
        }
    }

    /// <summary>
    /// Represents a single opponent hand slot and associated inference metadata.
    /// </summary>
    public sealed class OpponentHandSlot
    {
        public OpponentHandSlot(
            int position,
            HandCardIdentity? knownCard,
            bool wasKeptInMulligan,
            IEnumerable<CardOriginTag>? origins,
            IEnumerable<CardCandidate>? candidateCards,
            DateTimeOffset? lastUpdated = null)
        {
            if(position < 0)
                throw new ArgumentOutOfRangeException(nameof(position), "Hand slot position must be non-negative.");

            Position = position;
            KnownCard = knownCard;
            WasKeptInMulligan = wasKeptInMulligan;
            Origins = ToReadOnlyList(origins);
            CandidateCards = ToReadOnlyList(candidateCards);
            LastUpdated = lastUpdated;
        }

        public int Position { get; }

        /// <summary>
        /// Known card identity when the slot has been revealed (null when still hidden).
        /// </summary>
        public HandCardIdentity? KnownCard { get; }

        public bool WasKeptInMulligan { get; }

        /// <summary>
        /// Origins describing how the card entered the opponent's hand.
        /// </summary>
        public IReadOnlyList<CardOriginTag> Origins { get; }

        /// <summary>
        /// Possible candidate cards (by id) for hidden slots.
        /// </summary>
        public IReadOnlyList<CardCandidate> CandidateCards { get; }

        public DateTimeOffset? LastUpdated { get; }

        public OpponentHandSlot With(
            HandCardIdentity? knownCard = null,
            bool? wasKeptInMulligan = null,
            IEnumerable<CardOriginTag>? origins = null,
            IEnumerable<CardCandidate>? candidateCards = null,
            DateTimeOffset? lastUpdated = null)
        {
            return new OpponentHandSlot(
                Position,
                knownCard ?? KnownCard,
                wasKeptInMulligan ?? WasKeptInMulligan,
                origins ?? Origins,
                candidateCards ?? CandidateCards,
                lastUpdated ?? LastUpdated);
        }

        private static IReadOnlyList<T> ToReadOnlyList<T>(IEnumerable<T>? source)
        {
            if(source == null)
                return Array.Empty<T>();

            if(source is IReadOnlyList<T> readOnly)
                return readOnly;

            var list = new List<T>();

            foreach(var item in source)
                list.Add(item);

            return new ReadOnlyCollection<T>(list);
        }
    }

    /// <summary>
    /// Unique identity for a revealed opponent hand card.
    /// </summary>
    public sealed class HandCardIdentity
    {
        public HandCardIdentity(int? entityId, string? cardId, string? cardName)
        {
            EntityId = entityId;
            CardId = cardId;
            CardName = cardName;
        }

        public int? EntityId { get; }

        public string? CardId { get; }

        public string? CardName { get; }
    }

    public sealed class CardOriginTag
    {
        public CardOriginTag(CardOriginType type, string? sourceCardId = null, string? sourceCardName = null)
        {
            Type = type;
            SourceCardId = sourceCardId;
            SourceCardName = sourceCardName;
        }

        public CardOriginType Type { get; }

        public string? SourceCardId { get; }

        public string? SourceCardName { get; }
    }

    public enum CardOriginType
    {
        Unknown = 0,
        OriginalDeck,
        KeptInMulligan,
        Created,
        Discovered,
        Generated,
        ReturnedToHand
    }

    public sealed class CardCandidate
    {
        public CardCandidate(string cardId, double? probability = null, string? note = null)
        {
            if(string.IsNullOrWhiteSpace(cardId))
                throw new ArgumentException("cardId must be provided.", nameof(cardId));

            CardId = cardId;
            Probability = probability;
            Note = note;
        }

        public string CardId { get; }

        /// <summary>
        /// Optional probability in range [0,1]; null when not yet computed.
        /// </summary>
        public double? Probability { get; }

        /// <summary>
        /// Optional hint (e.g., "Created by Drakonic Studies").
        /// </summary>
        public string? Note { get; }
    }

    public sealed class SecretCandidateGroup
    {
        public SecretCandidateGroup(
            string? cardClass,
            IEnumerable<SecretCandidate> candidates,
            DateTimeOffset? lastEvaluated = null,
            int? entityId = null)
        {
            CardClass = cardClass;
            Candidates = ToReadOnlyList(candidates);
            LastEvaluated = lastEvaluated;
            EntityId = entityId;
        }

        public string? CardClass { get; }

        public IReadOnlyList<SecretCandidate> Candidates { get; }

        public DateTimeOffset? LastEvaluated { get; }

        public int? EntityId { get; }

        private static IReadOnlyList<T> ToReadOnlyList<T>(IEnumerable<T>? source)
        {
            if(source == null)
                return Array.Empty<T>();

            if(source is IReadOnlyList<T> readOnly)
                return readOnly;

            var list = new List<T>();

            foreach(var item in source)
                list.Add(item);

            return new ReadOnlyCollection<T>(list);
        }

        public static SecretCandidateGroup Empty { get; } = new SecretCandidateGroup(null, Array.Empty<SecretCandidate>());
    }

    public sealed class SecretCandidate
    {
        public SecretCandidate(string cardId, double? probability = null, string? status = null)
        {
            if(string.IsNullOrWhiteSpace(cardId))
                throw new ArgumentException("cardId must be provided.", nameof(cardId));

            CardId = cardId;
            Probability = probability;
            Status = status;
        }

        public string CardId { get; }

        /// <summary>
        /// Optional probability in range [0,1]; null when not yet evaluated.
        /// </summary>
        public double? Probability { get; }

        /// <summary>
        /// Optional status (e.g., "eliminated", "pending trigger").
        /// </summary>
        public string? Status { get; }
    }
}
