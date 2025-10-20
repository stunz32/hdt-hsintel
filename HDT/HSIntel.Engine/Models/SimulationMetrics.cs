using System;
using HSIntel.Core.Models;

namespace HSIntel.Engine.Models
{
    /// <summary>
    /// Aggregated metrics describing incremental effects of a simulated action.
    /// </summary>
    public sealed class SimulationMetrics
    {
        public static SimulationMetrics Empty { get; } = new SimulationMetrics();

        public SimulationMetrics(
            int friendlyCardsPlayed = 0,
            int friendlyAttacks = 0,
            int friendlyManaSpent = 0,
            int friendlyHeroPowerUses = 0,
            int friendlyMinionsRemoved = 0,
            int opponentMinionsRemoved = 0,
            int damageDealtToOpponentHero = 0,
            int damageTakenByFriendlyHero = 0)
        {
            FriendlyCardsPlayed = friendlyCardsPlayed;
            FriendlyAttacks = friendlyAttacks;
            FriendlyManaSpent = friendlyManaSpent;
            FriendlyHeroPowerUses = friendlyHeroPowerUses;
            FriendlyMinionsRemoved = friendlyMinionsRemoved;
            OpponentMinionsRemoved = opponentMinionsRemoved;
            DamageDealtToOpponentHero = damageDealtToOpponentHero;
            DamageTakenByFriendlyHero = damageTakenByFriendlyHero;
        }

        public int FriendlyCardsPlayed { get; }

        public int FriendlyAttacks { get; }

        public int FriendlyManaSpent { get; }

        public int FriendlyHeroPowerUses { get; }

        public int FriendlyMinionsRemoved { get; }

        public int OpponentMinionsRemoved { get; }

        public int DamageDealtToOpponentHero { get; }

        public int DamageTakenByFriendlyHero { get; }

        public SimulationMetrics Add(SimulationMetrics other)
        {
            if(other == null)
                return this;

            return new SimulationMetrics(
                FriendlyCardsPlayed + other.FriendlyCardsPlayed,
                FriendlyAttacks + other.FriendlyAttacks,
                FriendlyManaSpent + other.FriendlyManaSpent,
                FriendlyHeroPowerUses + other.FriendlyHeroPowerUses,
                FriendlyMinionsRemoved + other.FriendlyMinionsRemoved,
                OpponentMinionsRemoved + other.OpponentMinionsRemoved,
                DamageDealtToOpponentHero + other.DamageDealtToOpponentHero,
                DamageTakenByFriendlyHero + other.DamageTakenByFriendlyHero);
        }
    }

    /// <summary>
    /// Outcome produced by the board simulator after applying a candidate action.
    /// </summary>
    public sealed class SimulationResult
    {
        public SimulationResult(GameContext context, SimulationMetrics metrics, bool isValid, string? failureReason = null)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            Metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            IsValid = isValid;
            FailureReason = failureReason;
        }

        public GameContext Context { get; }

        public SimulationMetrics Metrics { get; }

        public bool IsValid { get; }

        public string? FailureReason { get; }
    }
}
