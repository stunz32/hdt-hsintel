using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace HSIntel.Core.Models.Diagnostics
{
    public sealed class StateValidationResult
    {
        private StateValidationResult(
            bool isValid,
            IReadOnlyList<StateValidationIssue> issues,
            DateTimeOffset timestamp)
        {
            IsValid = isValid;
            Issues = issues;
            Timestamp = timestamp;
        }

        public bool IsValid { get; }

        public IReadOnlyList<StateValidationIssue> Issues { get; }

        public DateTimeOffset Timestamp { get; }

        public static StateValidationResult Success(DateTimeOffset timestamp) =>
            new StateValidationResult(true, Array.Empty<StateValidationIssue>(), timestamp);

        public static StateValidationResult Failure(IEnumerable<StateValidationIssue> issues, DateTimeOffset timestamp)
        {
            if(issues == null)
                throw new ArgumentNullException(nameof(issues));

            IReadOnlyList<StateValidationIssue> list;

            if(issues is IReadOnlyList<StateValidationIssue> readOnly)
            {
                list = readOnly;
            }
            else
            {
                var builder = new List<StateValidationIssue>();

                foreach(var issue in issues)
                    builder.Add(issue);

                list = new ReadOnlyCollection<StateValidationIssue>(builder);
            }

            return new StateValidationResult(false, list, timestamp);
        }
    }

    public sealed class StateValidationIssue
    {
        public StateValidationIssue(string code, string message, ValidationSeverity severity)
        {
            Code = code;
            Message = message;
            Severity = severity;
        }

        public string Code { get; }

        public string Message { get; }

        public ValidationSeverity Severity { get; }
    }

    public enum ValidationSeverity
    {
        Warning = 0,
        Error = 1
    }

    public sealed class StateUpdateMetrics
    {
        private DateTimeOffset? _firstUpdate;
        private DateTimeOffset? _lastUpdate;
        private long _updateCount;

        public long UpdateCount => _updateCount;

        public TimeSpan? Elapsed => _firstUpdate.HasValue && _lastUpdate.HasValue
            ? _lastUpdate.Value - _firstUpdate.Value
            : null;

        public double UpdatesPerSecond
        {
            get
            {
                if(!_firstUpdate.HasValue || !_lastUpdate.HasValue || _updateCount <= 1)
                    return 0.0;

                var elapsedSeconds = (_lastUpdate.Value - _firstUpdate.Value).TotalSeconds;
                return elapsedSeconds <= 0 ? 0.0 : _updateCount / elapsedSeconds;
            }
        }

        public DateTimeOffset? LastUpdate => _lastUpdate;

        public void Record(DateTimeOffset timestamp)
        {
            if(!_firstUpdate.HasValue)
                _firstUpdate = timestamp;

            _lastUpdate = timestamp;
            _updateCount++;
        }

        public StateUpdateMetricsSnapshot CreateSnapshot() => new StateUpdateMetricsSnapshot(
            _updateCount,
            _firstUpdate,
            _lastUpdate,
            UpdatesPerSecond);
    }

    public readonly struct StateUpdateMetricsSnapshot
    {
        public StateUpdateMetricsSnapshot(
            long updateCount,
            DateTimeOffset? firstUpdate,
            DateTimeOffset? lastUpdate,
            double updatesPerSecond)
        {
            UpdateCount = updateCount;
            FirstUpdate = firstUpdate;
            LastUpdate = lastUpdate;
            UpdatesPerSecond = updatesPerSecond;
        }

        public long UpdateCount { get; }

        public DateTimeOffset? FirstUpdate { get; }

        public DateTimeOffset? LastUpdate { get; }

        public double UpdatesPerSecond { get; }
    }
}
