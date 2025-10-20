using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using HSIntel.Core.Models.Events;

namespace HSIntel.Core.Models
{
    /// <summary>
    /// Snapshot of cards held by a participant, including playability metadata.
    /// </summary>
    public sealed class HandState
    {
        private HandState(
            ParticipantSide owner,
            IReadOnlyList<HandCardState> cards,
            IReadOnlyList<HandCardState> playableCards,
            int? totalCardCount,
            DataQuality dataQuality,
            DateTimeOffset? lastUpdated)
        {
            Owner = owner;
            Cards = cards;
            PlayableCards = playableCards;
            TotalCardCount = totalCardCount;
            DataQuality = dataQuality;
            LastUpdated = lastUpdated;
        }

        public ParticipantSide Owner { get; }

        public IReadOnlyList<HandCardState> Cards { get; }

        public IReadOnlyList<HandCardState> PlayableCards { get; }

        public int? TotalCardCount { get; }

        public DataQuality DataQuality { get; }

        public DateTimeOffset? LastUpdated { get; }

        public static HandState Create(
            ParticipantSide owner,
            IEnumerable<HandCardState>? cards = null,
            int? totalCardCount = null,
            DataQuality dataQuality = DataQuality.Normal,
            DateTimeOffset? lastUpdated = null)
        {
            var cardList = ToReadOnlyList(cards);
            var playableCards = ExtractPlayable(cardList);
            var effectiveCount = totalCardCount ?? cardList.Count;

            return new HandState(owner, cardList, playableCards, effectiveCount, dataQuality, lastUpdated);
        }

        public static HandState CreateEmpty(ParticipantSide owner) =>
            Create(owner);

        public HandState With(
            IEnumerable<HandCardState>? cards = null,
            int? totalCardCount = null,
            DataQuality? dataQuality = null,
            DateTimeOffset? lastUpdated = null)
        {
            var cardList = cards == null ? Cards : ToReadOnlyList(cards);
            var playableCards = ExtractPlayable(cardList);
            var effectiveCount = totalCardCount ?? TotalCardCount ?? cardList.Count;

            return new HandState(
                Owner,
                cardList,
                playableCards,
                effectiveCount,
                dataQuality ?? DataQuality,
                lastUpdated ?? LastUpdated);
        }

        private static IReadOnlyList<HandCardState> ExtractPlayable(IReadOnlyList<HandCardState> cards)
        {
            if(cards.Count == 0)
                return Array.Empty<HandCardState>();

            var playable = new List<HandCardState>();

            foreach(var card in cards)
            {
                if(card.IsPlayable)
                    playable.Add(card);
            }

            if(playable.Count == 0)
                return Array.Empty<HandCardState>();

            return new ReadOnlyCollection<HandCardState>(playable);
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
    /// Immutable representation of a single card in hand.
    /// </summary>
    public sealed class HandCardState
    {
        public HandCardState(
            ParticipantSide controller,
            int? entityId,
            string? cardId,
            string? cardName,
            int? baseCost,
            int? effectiveCost,
            bool isKnown,
            bool isPlayable,
            int? zonePosition = null,
            IEnumerable<CreatedByTag>? createdBy = null,
            bool wasGeneratedThisTurn = false)
        {
            Controller = controller;
            EntityId = entityId;
            CardId = cardId;
            CardName = cardName;
            BaseCost = baseCost;
            EffectiveCost = effectiveCost;
            IsKnown = isKnown;
            IsPlayable = isPlayable;
            ZonePosition = zonePosition;
            CreatedBy = ToReadOnlyList(createdBy);
            WasGeneratedThisTurn = wasGeneratedThisTurn;
        }

        public ParticipantSide Controller { get; }

        public int? EntityId { get; }

        public string? CardId { get; }

        public string? CardName { get; }

        public int? BaseCost { get; }

        public int? EffectiveCost { get; }

        public bool IsKnown { get; }

        public bool IsPlayable { get; }

        /// <summary>
        /// Zero-based hand slot index, where 0 is left-most.
        /// </summary>
        public int? ZonePosition { get; }

        public IReadOnlyList<CreatedByTag> CreatedBy { get; }

        public bool WasGeneratedThisTurn { get; }

        public bool HasKnownCardId => !string.IsNullOrWhiteSpace(CardId);

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
    /// Tracks the sources responsible for generating a hand card.
    /// </summary>
    public sealed class CreatedByTag
    {
        public CreatedByTag(string? sourceCardId, string? sourceCardName, int? sourceEntityId)
        {
            SourceCardId = sourceCardId;
            SourceCardName = sourceCardName;
            SourceEntityId = sourceEntityId;
        }

        public string? SourceCardId { get; }

        public string? SourceCardName { get; }

        public int? SourceEntityId { get; }
    }
}
