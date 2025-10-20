using System;
using HSIntel.Core.Models.Events;

namespace HSIntel.Core.Models
{
    /// <summary>
    /// Aggregated snapshot of the game suitable for downstream evaluation engines.
    /// </summary>
    public sealed class GameContext
    {
        public static GameContext Empty { get; } = new GameContext(
            BoardState.Empty,
            HandState.CreateEmpty(ParticipantSide.Friendly),
            HandState.CreateEmpty(ParticipantSide.Opponent),
            ManaState.Unknown(ParticipantSide.Friendly),
            ManaState.Unknown(ParticipantSide.Opponent),
            null,
            null,
            DataQuality.Normal,
            null,
            null);

        public GameContext(
            BoardState board,
            HandState friendlyHand,
            HandState opponentHand,
            ManaState friendlyMana,
            ManaState opponentMana,
            int? turnNumber,
            ParticipantSide? activeSide,
            DataQuality dataQuality,
            DateTimeOffset? lastUpdated,
            string? gameId)
        {
            Board = board ?? throw new ArgumentNullException(nameof(board));
            FriendlyHand = friendlyHand ?? throw new ArgumentNullException(nameof(friendlyHand));
            OpponentHand = opponentHand ?? throw new ArgumentNullException(nameof(opponentHand));
            FriendlyMana = friendlyMana ?? throw new ArgumentNullException(nameof(friendlyMana));
            OpponentMana = opponentMana ?? throw new ArgumentNullException(nameof(opponentMana));
            TurnNumber = turnNumber;
            ActiveSide = activeSide;
            DataQuality = dataQuality;
            LastUpdated = lastUpdated;
            GameId = gameId;
        }

        public BoardState Board { get; }

        public HandState FriendlyHand { get; }

        public HandState OpponentHand { get; }

        public ManaState FriendlyMana { get; }

        public ManaState OpponentMana { get; }

        public int? TurnNumber { get; }

        public ParticipantSide? ActiveSide { get; }

        public DataQuality DataQuality { get; }

        public DateTimeOffset? LastUpdated { get; }

        public string? GameId { get; }

        public bool IsFriendlyTurn => ActiveSide == ParticipantSide.Friendly;

        public GameContext With(
            BoardState? board = null,
            HandState? friendlyHand = null,
            HandState? opponentHand = null,
            ManaState? friendlyMana = null,
            ManaState? opponentMana = null,
            int? turnNumber = null,
            ParticipantSide? activeSide = null,
            DataQuality? dataQuality = null,
            DateTimeOffset? lastUpdated = null,
            string? gameId = null)
        {
            return new GameContext(
                board ?? Board,
                friendlyHand ?? FriendlyHand,
                opponentHand ?? OpponentHand,
                friendlyMana ?? FriendlyMana,
                opponentMana ?? OpponentMana,
                turnNumber ?? TurnNumber,
                activeSide ?? ActiveSide,
                dataQuality ?? DataQuality,
                lastUpdated ?? LastUpdated,
                gameId ?? GameId);
        }
    }

    /// <summary>
    /// Tracks a participant's mana resources for the current turn.
    /// </summary>
    public sealed class ManaState
    {
        public ManaState(
            ParticipantSide owner,
            int? total,
            int? available,
            int? overloaded = null,
            int? locked = null,
            int? temporary = null)
        {
            Owner = owner;
            Total = total;
            Available = available;
            Overloaded = overloaded;
            Locked = locked;
            Temporary = temporary;
        }

        public ParticipantSide Owner { get; }

        public int? Total { get; }

        public int? Available { get; }

        public int? Overloaded { get; }

        public int? Locked { get; }

        public int? Temporary { get; }

        public static ManaState Unknown(ParticipantSide owner) =>
            new ManaState(owner, null, null);

        public ManaState With(
            int? total = null,
            int? available = null,
            int? overloaded = null,
            int? locked = null,
            int? temporary = null)
        {
            return new ManaState(
                Owner,
                total ?? Total,
                available ?? Available,
                overloaded ?? Overloaded,
                locked ?? Locked,
                temporary ?? Temporary);
        }
    }
}
