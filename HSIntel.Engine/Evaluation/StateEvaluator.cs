using System;
using System.Diagnostics;
using System.Linq;
using HSIntel.Core.Models;
using HSIntel.Engine.Models;

namespace HSIntel.Engine.Evaluation
{
    /// <summary>
    /// Scores prospective game states using a weighted heuristic model.
    /// </summary>
    public sealed class StateEvaluator
    {
        private static readonly EvaluationWeights DefaultWeights = new EvaluationWeights(
            boardControl: 0.30,
            cardAdvantage: 0.15,
            healthDifferential: 0.20,
            manaCurve: 0.10,
            tempoGain: 0.15,
            lethalThreat: 0.10);

        private readonly EvaluationWeights _weights;

        public StateEvaluator(EvaluationWeights? weights = null)
        {
            _weights = weights ?? DefaultWeights;
        }

        public EvaluationResult Evaluate(GameContext context, SimulationMetrics? metrics = null)
        {
            if(context == null)
                throw new ArgumentNullException(nameof(context));

            metrics ??= SimulationMetrics.Empty;

            var boardControl = ComputeBoardControl(context);
            var cardAdvantage = ComputeCardAdvantage(context);
            var healthDifferential = ComputeHealthDifferential(context);
            var manaCurve = ComputeManaCurve(context, metrics);
            var tempoGain = ComputeTempoGain(metrics);
            var lethalThreat = ComputeLethalThreat(context);

            var breakdown = new EvaluationBreakdown(
                boardControl,
                cardAdvantage,
                healthDifferential,
                manaCurve,
                tempoGain,
                lethalThreat);

            var totalScore =
                boardControl * _weights.BoardControl +
                cardAdvantage * _weights.CardAdvantage +
                healthDifferential * _weights.HealthDifferential +
                manaCurve * _weights.ManaCurve +
                tempoGain * _weights.TempoGain +
                lethalThreat * _weights.LethalThreat;

            var result = new EvaluationResult(totalScore, breakdown);

            Trace.WriteLine($"[HSIntel][Engine] Evaluate => score={totalScore:F2} board={boardControl:F2} card={cardAdvantage:F2} health={healthDifferential:F2} tempo={tempoGain:F2} lethal={lethalThreat:F2}");
            return result;
        }

        private static double ComputeBoardControl(GameContext context)
        {
            var friendly = context.Board.FriendlyMinions.Sum(m => (m.Attack ?? 0) + (m.Health ?? 0) + (m.HasTaunt ? 2 : 0));
            var opponent = context.Board.OpponentMinions.Sum(m => (m.Attack ?? 0) + (m.Health ?? 0) + (m.HasTaunt ? 2 : 0));
            var delta = friendly - opponent;
            return delta / 10.0;
        }

        private static double ComputeCardAdvantage(GameContext context)
        {
            var friendly = context.FriendlyHand.TotalCardCount ?? context.FriendlyHand.Cards.Count;
            var opponent = context.OpponentHand.TotalCardCount ?? context.OpponentHand.Cards.Count;
            var delta = friendly - opponent;
            return delta / 2.0;
        }

        private static double ComputeHealthDifferential(GameContext context)
        {
            double FriendlyTotal(HeroState hero) => (hero.Health ?? 0) + (hero.Armor ?? 0);

            var friendly = FriendlyTotal(context.Board.FriendlyHero);
            var opponent = FriendlyTotal(context.Board.OpponentHero);
            return (friendly - opponent) / 10.0;
        }

        private static double ComputeManaCurve(GameContext context, SimulationMetrics metrics)
        {
            var total = context.FriendlyMana.Total ?? context.FriendlyMana.Available ?? 0;
            if(total <= 0)
                return 0;

            var spent = metrics.FriendlyManaSpent;
            return Math.Min(1.5, spent / Math.Max(1.0, total));
        }

        private static double ComputeTempoGain(SimulationMetrics metrics)
        {
            var value =
                metrics.FriendlyCardsPlayed * 0.5 +
                metrics.FriendlyAttacks * 0.4 +
                metrics.OpponentMinionsRemoved * 0.8 -
                metrics.FriendlyMinionsRemoved * 0.7 +
                metrics.DamageDealtToOpponentHero * 0.05;

            return value / 2.0;
        }

        private static double ComputeLethalThreat(GameContext context)
        {
            var boardAttack = context.Board.FriendlyMinions.Sum(m => m.Attack ?? 0);
            var heroAttack = context.Board.FriendlyHero.Weapon?.Attack ?? 0;
            var totalAttack = boardAttack + heroAttack;

            var opponentHero = context.Board.OpponentHero;
            var opponentHealth = (opponentHero.Health ?? 0) + (opponentHero.Armor ?? 0);

            if(opponentHealth <= 0)
                return 2.0;

            if(totalAttack >= opponentHealth)
                return 1.5;

            return totalAttack / Math.Max(1.0, opponentHealth);
        }
    }
}
