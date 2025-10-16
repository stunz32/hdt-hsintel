using System;
using HSIntel.Core.Models.Events;

namespace HSIntel.Core.Models.Events.Raw
{
    public sealed class RawHandOddsSnapshot
    {
        public RawHandOddsSnapshot(
            int handCount,
            int deckCount,
            bool hasCoin,
            double? singleCopyProbability,
            double? doubleCopyProbability,
            DateTimeOffset timestamp,
            ParticipantSide activeSide)
        {
            HandCount = handCount;
            DeckCount = deckCount;
            HasCoin = hasCoin;
            SingleCopyProbability = singleCopyProbability;
            DoubleCopyProbability = doubleCopyProbability;
            Timestamp = timestamp;
            ActiveSide = activeSide;
        }

        public int HandCount { get; }

        public int DeckCount { get; }

        public bool HasCoin { get; }

        public double? SingleCopyProbability { get; }

        public double? DoubleCopyProbability { get; }

        public DateTimeOffset Timestamp { get; }

        public ParticipantSide ActiveSide { get; }
    }
}
