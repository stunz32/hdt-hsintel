using System;
using System.Collections.Generic;
using HSIntel.Engine.Models;

namespace HSIntel.Engine.Models
{
    /// <summary>
    /// One step in a recommended play sequence. Wraps a previously simulated action.
    /// </summary>
    internal sealed class PlayRecommendationStep
    {
        public PlayRecommendationStep(int order, GameAction action)
        {
            if(order <= 0)
                throw new ArgumentOutOfRangeException(nameof(order));
            Action = action ?? throw new ArgumentNullException(nameof(action));
            Order = order;
        }

        public int Order { get; }

        public GameAction Action { get; }

        public override string ToString() => $"{Order}: {Action}";
    }

    /// <summary>
    /// Immutable container for a full set of recommended plays derived from a SearchResult.
    /// Internal-only; presentation is handled by overlay components without changing Engine public API.
    /// </summary>
    internal sealed class PlayRecommendationSet
    {
        public PlayRecommendationSet(
            IReadOnlyList<PlayRecommendationStep> steps,
            bool isLethal,
            double totalScore,
            int depth,
            int nodesEvaluated,
            TimeSpan elapsed,
            bool timedOut,
            DateTimeOffset generatedAt)
        {
            Steps = steps ?? throw new ArgumentNullException(nameof(steps));
            IsLethal = isLethal;
            TotalScore = totalScore;
            Depth = depth;
            NodesEvaluated = nodesEvaluated;
            Elapsed = elapsed;
            TimedOut = timedOut;
            GeneratedAt = generatedAt;
        }

        public IReadOnlyList<PlayRecommendationStep> Steps { get; }

        public bool IsLethal { get; }

        public double TotalScore { get; }

        public int Depth { get; }

        public int NodesEvaluated { get; }

        public TimeSpan Elapsed { get; }

        public bool TimedOut { get; }

        public DateTimeOffset GeneratedAt { get; }
    }
}
