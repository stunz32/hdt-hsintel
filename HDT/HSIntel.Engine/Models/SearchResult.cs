using System;
using System.Collections.Generic;
using HSIntel.Core.Models;
using HSIntel.Engine.Lethal;

namespace HSIntel.Engine.Models
{
    public readonly struct TranspositionStats
    {
        public TranspositionStats(int probes, int hits, int stores, int evictions, int size)
        {
            Probes = probes;
            Hits = hits;
            Stores = stores;
            Evictions = evictions;
            Size = size;
        }

        public int Probes { get; }

        public int Hits { get; }

        public int Stores { get; }

        public int Evictions { get; }

        public int Size { get; }
    }

    public sealed class SearchResult
    {
        public SearchResult(
            GameContext initialContext,
            GameContext finalContext,
            IReadOnlyList<GameAction> actions,
            EvaluationResult evaluation,
            SimulationMetrics metrics,
            int nodesEvaluated,
            int depthReached,
            TimeSpan elapsed,
            bool timedOut,
            TranspositionStats transpositionStats,
            LethalResult? lethalResult = null)
        {
            InitialContext = initialContext ?? throw new ArgumentNullException(nameof(initialContext));
            FinalContext = finalContext ?? throw new ArgumentNullException(nameof(finalContext));
            Actions = actions ?? throw new ArgumentNullException(nameof(actions));
            Evaluation = evaluation ?? throw new ArgumentNullException(nameof(evaluation));
            Metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            NodesEvaluated = nodesEvaluated;
            DepthReached = depthReached;
            Elapsed = elapsed;
            TimedOut = timedOut;
            TranspositionStats = transpositionStats;
            LethalResult = lethalResult;
        }

        public GameContext InitialContext { get; }

        public GameContext FinalContext { get; }

        public IReadOnlyList<GameAction> Actions { get; }

        public EvaluationResult Evaluation { get; }

        public SimulationMetrics Metrics { get; }

        public int NodesEvaluated { get; }

        public int DepthReached { get; }

        public TimeSpan Elapsed { get; }

        public bool TimedOut { get; }

        public TranspositionStats TranspositionStats { get; }

        public LethalResult? LethalResult { get; }
    }
}
