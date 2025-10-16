using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using HSIntel.Core.Config;
using HSIntel.Core.Models;
using HSIntel.Engine.Evaluation;
using HSIntel.Engine.Models;
using HSIntel.Engine.MoveGen;
using HSIntel.Engine.Simulator;

namespace HSIntel.Engine.Search
{
    /// <summary>
    /// Performs a fixed-width beam search over generated action sequences.
    /// </summary>
    public sealed class BeamSearch
    {
        private sealed class BeamNode
        {
            public BeamNode(GameContext context, List<GameAction> actions, SimulationMetrics metrics, EvaluationResult evaluation)
            {
                Context = context;
                Actions = actions;
                Metrics = metrics;
                Evaluation = evaluation;
            }

            public GameContext Context { get; }

            public List<GameAction> Actions { get; }

            public SimulationMetrics Metrics { get; }

            public EvaluationResult Evaluation { get; }
        }

        private readonly MoveGenerator _moveGenerator;
        private readonly BoardSimulator _boardSimulator;
        private readonly StateEvaluator _stateEvaluator;

        public BeamSearch(MoveGenerator moveGenerator, BoardSimulator boardSimulator, StateEvaluator stateEvaluator)
        {
            _moveGenerator = moveGenerator ?? throw new ArgumentNullException(nameof(moveGenerator));
            _boardSimulator = boardSimulator ?? throw new ArgumentNullException(nameof(boardSimulator));
            _stateEvaluator = stateEvaluator ?? throw new ArgumentNullException(nameof(stateEvaluator));
        }

        public SearchResult Search(GameContext context, HSIntelConfig config, CancellationToken cancellationToken = default)
        {
            if(context == null)
                throw new ArgumentNullException(nameof(context));
            if(config == null)
                throw new ArgumentNullException(nameof(config));

            var beamWidth = Math.Max(1, config.BeamWidth);
            var depthLimit = Math.Max(1, config.Depth);
            var timeLimitMs = Math.Max(50, config.MaxComputeMs);

            var stopwatch = Stopwatch.StartNew();
            var transpositions = new TranspositionTable();

            var rootMetrics = SimulationMetrics.Empty;
            var rootEvaluation = _stateEvaluator.Evaluate(context, rootMetrics);

            var frontier = new List<BeamNode>
            {
                new BeamNode(context, new List<GameAction>(), rootMetrics, rootEvaluation)
            };

            var bestNode = frontier[0];
            var nodesEvaluated = 0;
            var depthReached = 0;

            for(var depth = 0; depth < depthLimit; depth++)
            {
                if(cancellationToken.IsCancellationRequested || stopwatch.ElapsedMilliseconds >= timeLimitMs)
                    break;

                var nextFrontier = new List<BeamNode>();

                foreach(var node in frontier)
                {
                    if(cancellationToken.IsCancellationRequested || stopwatch.ElapsedMilliseconds >= timeLimitMs)
                        break;

                    var actions = _moveGenerator.Generate(node.Context);
                    foreach(var action in actions)
                    {
                        if(cancellationToken.IsCancellationRequested || stopwatch.ElapsedMilliseconds >= timeLimitMs)
                            break;

                        var simulation = _boardSimulator.Simulate(node.Context, action);
                        if(!simulation.IsValid)
                            continue;

                        var aggregatedMetrics = node.Metrics.Add(simulation.Metrics);
                        var evaluation = _stateEvaluator.Evaluate(simulation.Context, aggregatedMetrics);

                        var sequence = new List<GameAction>(node.Actions.Count + 1);
                        sequence.AddRange(node.Actions);
                        sequence.Add(action);

                        var hash = transpositions.ComputeHash(simulation.Context, aggregatedMetrics);
                        if(transpositions.TryGet(hash, sequence.Count, out var existingScore) && existingScore >= evaluation.TotalScore)
                            continue;

                        transpositions.Store(hash, sequence.Count, evaluation.TotalScore);

                        var nextNode = new BeamNode(simulation.Context, sequence, aggregatedMetrics, evaluation);
                        nextFrontier.Add(nextNode);

                        nodesEvaluated++;
                        depthReached = Math.Max(depthReached, sequence.Count);

                        if(evaluation.TotalScore > bestNode.Evaluation.TotalScore)
                            bestNode = nextNode;
                    }
                }

                if(nextFrontier.Count == 0)
                    break;

                frontier = nextFrontier
                    .OrderByDescending(n => n.Evaluation.TotalScore)
                    .ThenBy(n => n.Actions.Count)
                    .Take(beamWidth)
                    .ToList();

                transpositions.Prune(beamWidth * depthLimit * 8);
            }

            stopwatch.Stop();

            var timedOut = cancellationToken.IsCancellationRequested || stopwatch.ElapsedMilliseconds >= timeLimitMs;
            var stats = transpositions.Snapshot();

            Trace.WriteLine($"[HSIntel][Engine] BeamSearch explored {nodesEvaluated} nodes, depth={depthReached}, bestScore={bestNode.Evaluation.TotalScore:F2}, timedOut={timedOut}");

            return new SearchResult(
                context,
                bestNode.Context,
                bestNode.Actions,
                bestNode.Evaluation,
                bestNode.Metrics,
                nodesEvaluated,
                depthReached,
                stopwatch.Elapsed,
                timedOut,
                stats);
        }
    }
}
