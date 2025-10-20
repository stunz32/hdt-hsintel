namespace HSIntel.Core.Models.Events
{
    public sealed class CardSnapshot
    {
        public CardSnapshot(
            ParticipantSide controller,
            string? cardId,
            string? cardName,
            int? entityId,
            bool isKnown)
        {
            Controller = controller;
            CardId = cardId;
            CardName = cardName;
            EntityId = entityId;
            IsKnown = isKnown;
        }

        public ParticipantSide Controller { get; }

        public string? CardId { get; }

        public string? CardName { get; }

        public int? EntityId { get; }

        public bool IsKnown { get; }

        public bool HasCardId => !string.IsNullOrWhiteSpace(CardId);

        public CardSnapshot WithEntityId(int? entityId) =>
            new CardSnapshot(Controller, CardId, CardName, entityId, IsKnown);
    }
}
