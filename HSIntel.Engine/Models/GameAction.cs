using System;
using HSIntel.Core.Models;
using HSIntel.Core.Models.Events;

namespace HSIntel.Engine.Models
{
    public enum GameActionType
    {
        PlayCard = 0,
        Attack,
        HeroPower,
        EndTurn
    }

    /// <summary>
    /// Target metadata associated with an action.
    /// </summary>
    public sealed class ActionTarget
    {
        public ActionTarget(ParticipantSide side, int? entityId, bool isHero, string? cardId, string? cardName)
        {
            Side = side;
            EntityId = entityId;
            IsHero = isHero;
            CardId = cardId;
            CardName = cardName;
        }

        public ParticipantSide Side { get; }

        public int? EntityId { get; }

        public bool IsHero { get; }

        public string? CardId { get; }

        public string? CardName { get; }

        public override string ToString()
        {
            var name = string.IsNullOrWhiteSpace(CardName)
                ? CardId ?? (IsHero ? "Hero" : "Entity")
                : CardName;
            return $"{Side}:{name ?? "Unknown"}#{EntityId?.ToString() ?? "?"}";
        }
    }

    /// <summary>
    /// Immutable representation of an action considered by the decision engine.
    /// </summary>
    public sealed class GameAction
    {
        public GameAction(
            GameActionType actionType,
            string description,
            int priority,
            HandCardState? card = null,
            MinionState? attacker = null,
            ActionTarget? target = null,
            bool requiresTarget = false)
        {
            ActionType = actionType;
            Description = description ?? throw new ArgumentNullException(nameof(description));
            Priority = priority;
            Card = card;
            Attacker = attacker;
            Target = target;
            RequiresTarget = requiresTarget;
        }

        public GameActionType ActionType { get; }

        public string Description { get; }

        /// <summary>
        /// Higher values indicate this action should be explored earlier.
        /// </summary>
        public int Priority { get; }

        public HandCardState? Card { get; }

        public MinionState? Attacker { get; }

        public ActionTarget? Target { get; }

        public bool RequiresTarget { get; }

        public GameAction WithTarget(ActionTarget target)
        {
            if(target == null)
                throw new ArgumentNullException(nameof(target));

            return new GameAction(ActionType, Description, Priority, Card, Attacker, target, RequiresTarget);
        }

        public override string ToString() => $"{ActionType}: {Description}";
    }
}
