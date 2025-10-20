using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HSIntel.Core.Interfaces;
using HSIntel.Core.Models;
using HSIntel.Core.Models.Events;
using HSIntel.Core.Models.Events.Raw;

namespace HSIntel.Core.Services
{
    public sealed class HDTEventBinder : IDisposable
    {
        private enum DataQualityCause
        {
            None = 0,
            UpdateLatency,
            FriendlyPayloadMissing
        }

        private readonly IHdtEventSource _eventSource;
        private readonly Func<DateTimeOffset> _timestampProvider;
        private readonly TimeSpan _updateTolerance;
        private readonly TimeSpan _expectedUpdateInterval;
        private readonly TimeSpan _secretSnapshotThrottle = TimeSpan.FromMilliseconds(350);

        private bool _initialized;
        private bool _disposed;
        private DateTimeOffset? _lastUpdateTimestamp;
        private DataQuality _dataQuality = DataQuality.Normal;
        private DataQualityCause _dataQualityCause = DataQualityCause.None;
        private DateTimeOffset? _lastSecretSnapshotTimestamp;
        private string? _lastSecretSnapshotDigest;

        public HDTEventBinder(
            IHdtEventSource eventSource,
            Func<DateTimeOffset>? timestampProvider = null,
            TimeSpan? updateTolerance = null,
            TimeSpan? expectedUpdateInterval = null)
        {
            _eventSource = eventSource ?? throw new ArgumentNullException(nameof(eventSource));
            _timestampProvider = timestampProvider ?? (() => DateTimeOffset.UtcNow);
            _updateTolerance = updateTolerance ?? TimeSpan.FromMilliseconds(140);
            _expectedUpdateInterval = expectedUpdateInterval ?? TimeSpan.FromMilliseconds(100);
        }

        public event EventHandler<GameLifecycleEventArgs>? GameStarted;

        public event EventHandler<GameLifecycleEventArgs>? GameEnded;

        public event EventHandler<TurnStartedEventArgs>? TurnStarted;

        public event EventHandler<CardDrawnEventArgs>? CardDrawn;

        public event EventHandler<CardPlayedEventArgs>? CardPlayed;

        public event EventHandler<CardDiscardedEventArgs>? CardDiscarded;

        public event EventHandler<AttackEventArgs>? AttackOccurred;

        public event EventHandler<HeroPowerUsedEventArgs>? HeroPowerUsed;

        public event EventHandler<DeckSelectedEventArgs>? DeckSelected;

        public event EventHandler<UpdateTickEventArgs>? UpdateTick;

        public event EventHandler<BoardStateChangedEventArgs>? BoardStateChanged;

        public event EventHandler<DataQualityChangedEventArgs>? DataQualityChanged;

        public event EventHandler<OpponentMulliganEventArgs>? OpponentMulliganSummary;

        public event EventHandler<OpponentCardCreatedEventArgs>? OpponentCardCreated;

        public event EventHandler<OpponentSecretsChangedEventArgs>? OpponentSecretsChanged;

        public event EventHandler<OpponentHandOddsEventArgs>? OpponentHandOddsEvaluated;

        public DataQuality DataQuality => _dataQuality;

        public void Initialize()
        {
            if(_initialized)
                return;

            _eventSource.RegisterGameStarted(HandleGameStarted);
            _eventSource.RegisterGameEnded(HandleGameEnded);
            _eventSource.RegisterTurnStarted(HandleTurnStarted);
            _eventSource.RegisterPlayerCardDrawn(HandleCardDrawn);
            _eventSource.RegisterOpponentCardDrawn(HandleCardDrawn);
            _eventSource.RegisterPlayerCardPlayed(HandleCardPlayed);
            _eventSource.RegisterOpponentCardPlayed(HandleCardPlayed);
            _eventSource.RegisterPlayerCardDiscarded(HandleCardDiscarded);
            _eventSource.RegisterOpponentCardDiscarded(HandleCardDiscarded);
            _eventSource.RegisterAttackOccurred(HandleAttackOccurred);
            _eventSource.RegisterHeroPowerUsed(HandleHeroPowerUsed);
            _eventSource.RegisterDeckSelected(HandleDeckSelected);
            _eventSource.RegisterUpdateTick(HandleUpdateTick);
            _eventSource.RegisterOpponentMulliganSummary(HandleOpponentMulliganSummary);
            _eventSource.RegisterOpponentCardCreated(HandleOpponentCardCreated);
            _eventSource.RegisterSecretsChanged(HandleSecretsChanged);
            _eventSource.RegisterSecretsChanged(HandleSecretsChanged);

            _initialized = true;
        }

        public void Dispose()
        {
            if(_disposed)
                return;

            _disposed = true;
            _eventSource.Dispose();
        }

        private void HandleGameStarted()
        {
            var timestamp = Now();
            GameStarted?.Invoke(this, new GameLifecycleEventArgs(timestamp));
            EmitBoardStateChange(BoardStateChangeReason.GameStarted, null, null, timestamp);
            EmitOpponentSecretsSnapshot("GameStarted", timestamp, false);
        }

        private void HandleGameEnded()
        {
            var timestamp = Now();
            GameEnded?.Invoke(this, new GameLifecycleEventArgs(timestamp));
            EmitBoardStateChange(BoardStateChangeReason.GameEnded, null, null, timestamp);
            EmitOpponentSecretsSnapshot("GameEnded", timestamp, false);
        }

        private void HandleTurnStarted(RawTurnStartEvent rawEvent)
        {
            var timestamp = EnsureTimestamp(rawEvent.Timestamp);
            TurnStarted?.Invoke(this, new TurnStartedEventArgs(rawEvent.ActiveSide, rawEvent.TurnNumber, timestamp));
            EmitBoardStateChange(BoardStateChangeReason.TurnStarted, null, rawEvent.ActiveSide, timestamp);
            var cause = rawEvent.ActiveSide == ParticipantSide.Friendly ? "FriendlyTurnStart" : "OpponentTurnStart";
            EmitOpponentSecretsSnapshot(cause, timestamp, true);
            EmitOpponentHandOdds(rawEvent.ActiveSide, timestamp);
        }

        private void HandleCardDrawn(RawCardEvent rawEvent)
        {
            var timestamp = EnsureTimestamp(rawEvent.Timestamp);
            EvaluateFriendlyPayload(rawEvent.Card, BoardStateChangeReason.CardDrawn, timestamp);
            CardDrawn?.Invoke(this, new CardDrawnEventArgs(rawEvent.Card, timestamp));
            EmitBoardStateChange(BoardStateChangeReason.CardDrawn, rawEvent.Card, rawEvent.Controller, timestamp);
        }

        private void HandleCardPlayed(RawCardEvent rawEvent)
        {
            var timestamp = EnsureTimestamp(rawEvent.Timestamp);
            EvaluateFriendlyPayload(rawEvent.Card, BoardStateChangeReason.CardPlayed, timestamp);
            CardPlayed?.Invoke(this, new CardPlayedEventArgs(rawEvent.Card, timestamp));
            EmitBoardStateChange(BoardStateChangeReason.CardPlayed, rawEvent.Card, rawEvent.Controller, timestamp);
            var cause = rawEvent.Controller == ParticipantSide.Friendly ? "FriendlyCardPlayed" : "OpponentCardPlayed";
            EmitOpponentSecretsSnapshot(cause, timestamp, false);
        }

        private void HandleCardDiscarded(RawCardEvent rawEvent)
        {
            var timestamp = EnsureTimestamp(rawEvent.Timestamp);
            EvaluateFriendlyPayload(rawEvent.Card, BoardStateChangeReason.CardDiscarded, timestamp);
            CardDiscarded?.Invoke(this, new CardDiscardedEventArgs(rawEvent.Card, timestamp));
            EmitBoardStateChange(BoardStateChangeReason.CardDiscarded, rawEvent.Card, rawEvent.Controller, timestamp);
            if(rawEvent.Controller == ParticipantSide.Friendly)
                EmitOpponentSecretsSnapshot("FriendlyCardDiscarded", timestamp, false);
        }

        private void HandleAttackOccurred(RawAttackEvent rawEvent)
        {
            var timestamp = EnsureTimestamp(rawEvent.Timestamp);
            AttackOccurred?.Invoke(this, new AttackEventArgs(rawEvent.Attacker, rawEvent.Defender, timestamp));
            EmitBoardStateChange(BoardStateChangeReason.Attack, rawEvent.Attacker, rawEvent.Attacker.Controller, timestamp);
            if(rawEvent.Attacker.Controller == ParticipantSide.Friendly)
                EmitOpponentSecretsSnapshot("DirectAttack", timestamp, false);
        }

        private void HandleHeroPowerUsed(RawHeroPowerEvent rawEvent)
        {
            var timestamp = EnsureTimestamp(rawEvent.Timestamp);
            HeroPowerUsed?.Invoke(this, new HeroPowerUsedEventArgs(rawEvent.Controller, timestamp));
            EmitBoardStateChange(BoardStateChangeReason.HeroPower, null, rawEvent.Controller, timestamp);
            if(rawEvent.Controller == ParticipantSide.Friendly)
                EmitOpponentSecretsSnapshot("FriendlyHeroPower", timestamp, false);
        }

        private void HandleDeckSelected(RawDeckSelectedEvent rawEvent)
        {
            var timestamp = EnsureTimestamp(rawEvent.Timestamp);
            DeckSelected?.Invoke(this,
                new DeckSelectedEventArgs(rawEvent.DeckId, rawEvent.DeckName, rawEvent.CardCount, timestamp));
            EmitBoardStateChange(BoardStateChangeReason.DeckSelected, null, ParticipantSide.Friendly, timestamp);
        }

        private void HandleUpdateTick(RawUpdateTickEvent rawEvent)
        {
            var timestamp = EnsureTimestamp(rawEvent.Timestamp);
            var isFirstTick = !_lastUpdateTimestamp.HasValue;
            var elapsed = isFirstTick ? _expectedUpdateInterval : timestamp - _lastUpdateTimestamp!.Value;
            _lastUpdateTimestamp = timestamp;

            var isLate = !isFirstTick && elapsed > _updateTolerance;

            if(isLate)
            {
                SetDataQuality(
                    DataQuality.Degraded,
                    DataQualityCause.UpdateLatency,
                    $"Update tick exceeded tolerance ({elapsed.TotalMilliseconds:F0} ms).",
                    timestamp);
            }
            else if(_dataQuality == DataQuality.Degraded && _dataQualityCause == DataQualityCause.UpdateLatency)
            {
                SetDataQuality(
                    DataQuality.Normal,
                    DataQualityCause.UpdateLatency,
                    "Update tick back within tolerance.",
                    timestamp);
            }

            UpdateTick?.Invoke(this, new UpdateTickEventArgs(timestamp, elapsed, _dataQuality, isLate));
            EmitOpponentSecretsSnapshot(null, timestamp, true);
        }

        private void EvaluateFriendlyPayload(CardSnapshot snapshot, BoardStateChangeReason reason, DateTimeOffset timestamp)
        {
            if(snapshot.Controller != ParticipantSide.Friendly)
                return;

            if(!snapshot.HasCardId)
            {
                SetDataQuality(
                    DataQuality.Degraded,
                    DataQualityCause.FriendlyPayloadMissing,
                    $"{reason} missing card id for friendly side.",
                    timestamp);
            }
            else if(_dataQuality == DataQuality.Degraded && _dataQualityCause == DataQualityCause.FriendlyPayloadMissing)
            {
                SetDataQuality(
                    DataQuality.Normal,
                    DataQualityCause.FriendlyPayloadMissing,
                    $"{reason} payload restored.",
                    timestamp);
            }
        }

        private void HandleOpponentMulliganSummary(RawOpponentMulliganSummaryEvent rawEvent)
        {
            if(rawEvent == null)
                return;

            var slots = rawEvent.Slots
                .Select(slot => new OpponentMulliganSlotInfo(
                    slot.Position,
                    slot.WasKept,
                    slot.IsKnown,
                    slot.CardId,
                    slot.EntityId))
                .ToList()
                .AsReadOnly();

            OpponentMulliganSummary?.Invoke(
                this,
                new OpponentMulliganEventArgs(slots, rawEvent.CoinKept, EnsureTimestamp(rawEvent.Timestamp)));
        }

        private void HandleOpponentCardCreated(RawCardCreatedEvent rawEvent)
        {
            if(rawEvent == null)
                return;

            var candidateIds = rawEvent.CandidateCardIds ?? Array.Empty<string>();
            OpponentCardCreated?.Invoke(
                this,
                new OpponentCardCreatedEventArgs(
                    rawEvent.CardId,
                    rawEvent.EntityId,
                    rawEvent.CreatedByCardId,
                    rawEvent.CreatedByEntityId,
                rawEvent.Origin,
                candidateIds,
                rawEvent.Position,
                EnsureTimestamp(rawEvent.Timestamp)));
        }

        private void EmitOpponentSecretsSnapshot(string? cause, DateTimeOffset timestamp, bool allowThrottle)
        {
            if(allowThrottle && _lastSecretSnapshotTimestamp.HasValue)
            {
                var sinceLast = timestamp - _lastSecretSnapshotTimestamp.Value;
                if(sinceLast < _secretSnapshotThrottle)
                    return;
            }

            var snapshot = _eventSource.CaptureOpponentSecretsSnapshot();
            var groups = snapshot.Groups
                .Select(g =>
                {
                    var candidates = g.CandidateCardIds
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .Select(id => new SecretCandidate(id!))
                        .ToList();

                    return new SecretCandidateGroup(
                        g.CardClass,
                        candidates,
                        snapshot.Timestamp,
                        g.EntityId);
                })
                .ToList();

            var digest = BuildSecretDigest(groups);
            var hasChange = _lastSecretSnapshotDigest != digest;

            if(!hasChange && string.IsNullOrWhiteSpace(cause))
                return;

            _lastSecretSnapshotDigest = digest;
            _lastSecretSnapshotTimestamp = timestamp;

            OpponentSecretsChanged?.Invoke(
                this,
                new OpponentSecretsChangedEventArgs(groups, snapshot.Timestamp, cause));
        }

        private static string BuildSecretDigest(IReadOnlyList<SecretCandidateGroup> groups)
        {
            if(groups.Count == 0)
                return string.Empty;

            var ordered = groups
                .OrderBy(g => g.EntityId ?? int.MaxValue)
                .ThenBy(g => g.CardClass ?? string.Empty)
                .ToList();

            var builder = new StringBuilder();
            foreach(var group in ordered)
            {
                builder.Append(group.EntityId?.ToString() ?? "none");
                builder.Append(':');
                builder.Append(group.CardClass ?? "unknown");
                builder.Append('=');
                if(group.Candidates.Count == 0)
                {
                    builder.Append("[]");
                }
                else
                {
                    var ids = group.Candidates
                        .Select(c => c.CardId)
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .OrderBy(id => id, StringComparer.OrdinalIgnoreCase);
                    builder.Append(string.Join(",", ids));
                }
                builder.Append('|');
            }

            return builder.ToString();
        }

        private void HandleSecretsChanged()
        {
            var timestamp = Now();
            EmitOpponentSecretsSnapshot("SecretsChanged", timestamp, false);
        }

        private void EmitOpponentHandOdds(ParticipantSide activeSide, DateTimeOffset timestamp)
        {
            if(activeSide != ParticipantSide.Friendly)
                return;

            var snapshot = _eventSource.CaptureOpponentHandOddsSnapshot(activeSide);
            OpponentHandOddsEvaluated?.Invoke(
                this,
                new OpponentHandOddsEventArgs(
                    snapshot.SingleCopyProbability,
                    snapshot.DoubleCopyProbability,
                    snapshot.HandCount,
                    snapshot.DeckCount,
                    snapshot.HasCoin,
                    snapshot.ActiveSide,
                    snapshot.Timestamp));
        }

        private void EmitBoardStateChange(
            BoardStateChangeReason reason,
            CardSnapshot? subjectCard,
            ParticipantSide? participant,
            DateTimeOffset timestamp)
        {
            BoardStateChanged?.Invoke(this, new BoardStateChangedEventArgs(reason, timestamp, subjectCard, participant));
        }

        private void SetDataQuality(
            DataQuality newQuality,
            DataQualityCause cause,
            string reason,
            DateTimeOffset timestamp)
        {
            var shouldRaise = false;

            if(newQuality != _dataQuality)
            {
                shouldRaise = true;
            }
            else if(newQuality == DataQuality.Degraded && cause != _dataQualityCause)
            {
                shouldRaise = true;
            }
            else if(newQuality == DataQuality.Normal && _dataQualityCause != cause)
            {
                return;
            }

            if(!shouldRaise)
                return;

            _dataQuality = newQuality;
            _dataQualityCause = newQuality == DataQuality.Degraded ? cause : DataQualityCause.None;

            DataQualityChanged?.Invoke(this, new DataQualityChangedEventArgs(_dataQuality, reason, timestamp));
        }

        private DateTimeOffset EnsureTimestamp(DateTimeOffset timestamp) =>
            timestamp == default ? Now() : timestamp;

        private DateTimeOffset Now() => _timestampProvider();
    }
}



