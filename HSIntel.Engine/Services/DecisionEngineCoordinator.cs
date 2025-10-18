using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using HSIntel.Core.Config;
using HSIntel.Core.Models;
using HSIntel.Core.Models.Events;
using HSIntel.Core.Services;
using HSIntel.Engine.Evaluation;
using HSIntel.Engine.Models;
using HSIntel.Engine.MoveGen;
using HSIntel.Engine.Search;
using HSIntel.Engine.Simulator;

namespace HSIntel.Engine.Services
{
    /// <summary>
    /// Bridges StateBuilder updates to the decision engine search pipeline.
    /// </summary>
    public sealed class DecisionEngineCoordinator : IDisposable
    {
        private readonly StateBuilder _stateBuilder;
        private readonly Func<HSIntelConfig> _configProvider;
        private readonly MoveGenerator _moveGenerator;
        private readonly BoardSimulator _boardSimulator;
        private readonly StateEvaluator _stateEvaluator;
        private readonly BeamSearch _beamSearch;
        private readonly object _syncRoot = new object();

        private CancellationTokenSource? _evaluationCts;
        private bool _started;
        private bool _disposed;
        private DateTimeOffset _lastEvaluatedTimestamp = DateTimeOffset.MinValue;

        public DecisionEngineCoordinator(StateBuilder stateBuilder, Func<HSIntelConfig> configProvider)
        {
            _stateBuilder = stateBuilder ?? throw new ArgumentNullException(nameof(stateBuilder));
            _configProvider = configProvider ?? throw new ArgumentNullException(nameof(configProvider));
            _moveGenerator = new MoveGenerator();
            _boardSimulator = new BoardSimulator();
            _stateEvaluator = new StateEvaluator();
            _beamSearch = new BeamSearch(_moveGenerator, _boardSimulator, _stateEvaluator);
        }

        public event EventHandler<DecisionComputedEventArgs>? DecisionComputed;

        public event EventHandler<DecisionComputationFailedEventArgs>? DecisionFailed;

        public void Start()
        {
            if(_disposed)
                throw new ObjectDisposedException(nameof(DecisionEngineCoordinator));

            lock(_syncRoot)
            {
                if(_started)
                    return;

                _stateBuilder.GameContextUpdated += OnGameContextUpdated;
                _started = true;
            }

            Trace.WriteLine("[HSIntel][Engine] Decision engine coordinator started");
        }

        public void Stop()
        {
            lock(_syncRoot)
            {
                if(!_started)
                    return;

                _stateBuilder.GameContextUpdated -= OnGameContextUpdated;
                _started = false;
                _evaluationCts?.Cancel();
                _evaluationCts?.Dispose();
                _evaluationCts = null;
            }

            Trace.WriteLine("[HSIntel][Engine] Decision engine coordinator stopped");
        }

        private void OnGameContextUpdated(object? sender, GameContextUpdatedEventArgs e)
        {
            if(e.Context == null || e.Context.DataQuality == DataQuality.Degraded)
                return;
            if(e.Context.ActiveSide != ParticipantSide.Friendly)
                return;
            if(e.Timestamp <= _lastEvaluatedTimestamp)
                return;

            ScheduleEvaluation(e.Context, e.Timestamp);
        }

        private void ScheduleEvaluation(GameContext context, DateTimeOffset timestamp)
        {
            CancellationTokenSource? previousCts;
            CancellationToken cancellationToken;

            lock(_syncRoot)
            {
                previousCts = _evaluationCts;
                _evaluationCts = new CancellationTokenSource();
                cancellationToken = _evaluationCts.Token;
                _lastEvaluatedTimestamp = timestamp;
            }

            previousCts?.Cancel();
            previousCts?.Dispose();

            Trace.WriteLine($"[HSIntel][Engine] Scheduling evaluation at {timestamp:O}");

            Task.Run(() => EvaluateAsync(context, cancellationToken), cancellationToken);
        }

        private void EvaluateAsync(GameContext context, CancellationToken cancellationToken)
        {
            try
            {
                var config = _configProvider() ?? new HSIntelConfig();
                var result = _beamSearch.Search(context, config, cancellationToken);
                try
                {
                    var t = result.TranspositionStats;
                    Trace.WriteLine($"[HSIntel][Engine] BeamResult: best={result.Evaluation.TotalScore:F2} nodes={result.NodesEvaluated} depth={result.DepthReached} elapsed={result.Elapsed.TotalMilliseconds:F0}ms timedOut={result.TimedOut} ttable={t.Hits}/{t.Probes} hits size={t.Size}");
                }
                catch { }
                DecisionComputed?.Invoke(this, new DecisionComputedEventArgs(context, result));
            }
            catch(OperationCanceledException)
            {
                Trace.WriteLine("[HSIntel][Engine] Evaluation cancelled");
            }
            catch(Exception ex)
            {
                Trace.WriteLine($"[HSIntel][Engine] Evaluation failed: {ex}");
                DecisionFailed?.Invoke(this, new DecisionComputationFailedEventArgs(context, ex));
            }
        }

        public void Dispose()
        {
            if(_disposed)
                return;

            Stop();
            _disposed = true;
        }
    }
}
