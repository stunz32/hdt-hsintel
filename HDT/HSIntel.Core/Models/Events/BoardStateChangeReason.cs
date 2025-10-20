namespace HSIntel.Core.Models.Events
{
    public enum BoardStateChangeReason
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
        DeckSelected
    }
}
