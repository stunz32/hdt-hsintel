using System;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using HSIntel.Core.Models;
using HSIntel.Core.Models.Events;

namespace HSIntel.Core.Services
{
    /// <summary>
    /// Builds and maintains opponent-specific inference state.
    /// </summary>
    public sealed class OpponentStateBuilder : IDisposable
    {
        private readonly HDTEventBinder _binder;
        private readonly object _syncRoot = new();
        private bool _disposed;
        private bool _initialized;
        private OpponentModel _model = OpponentModel.Empty;

        public OpponentStateBuilder(HDTEventBinder binder)
        {
            _binder = binder ?? throw new ArgumentNullException(nameof(binder));
            Attach();
        }

        public event EventHandler<OpponentModelUpdatedEventArgs>? OpponentModelUpdated;

        public OpponentModel CurrentModel
        {
            get
            {
                lock(_syncRoot)
                    return _model;
            }
        }

        public void Dispose()
        {
            if(_disposed)
                return;

            _disposed = true;
            Detach();
        }

        private void Attach()
        {
            if(_initialized)
                return;

            _binder.GameStarted += HandleGameStarted;
            _binder.GameEnded += HandleGameEnded;
            _binder.OpponentMulliganSummary += HandleOpponentMulliganSummary;
            _binder.OpponentSecretsChanged += HandleOpponentSecretsChanged;
            _binder.OpponentCardCreated += HandleOpponentCardCreated;
            _binder.OpponentHandOddsEvaluated += HandleOpponentHandOddsEvaluated;
            _initialized = true;
        }

        private void Detach()
        {
            if(!_initialized)
                return;

            _binder.GameStarted -= HandleGameStarted;
            _binder.GameEnded -= HandleGameEnded;
            _binder.OpponentMulliganSummary -= HandleOpponentMulliganSummary;
            _binder.OpponentSecretsChanged -= HandleOpponentSecretsChanged;
            _binder.OpponentCardCreated -= HandleOpponentCardCreated;
            _binder.OpponentHandOddsEvaluated -= HandleOpponentHandOddsEvaluated;
            _initialized = false;
        }

        private void HandleGameStarted(object? sender, GameLifecycleEventArgs e)
        {
            ResetModel(e.Timestamp);
        }

        private void HandleGameEnded(object? sender, GameLifecycleEventArgs e)
        {
            ResetModel(e.Timestamp);
        }

        private void ResetModel(DateTimeOffset timestamp)
        {
            lock(_syncRoot)
            {
                _model = OpponentModel.Empty;
            }

            OpponentModelUpdated?.Invoke(this, new OpponentModelUpdatedEventArgs(CurrentModel, timestamp));
        }

        private void HandleOpponentMulliganSummary(object? sender, OpponentMulliganEventArgs e)
        {
            var slots = new List<OpponentHandSlot>(e.Slots.Count);
            var slotSummary = e.Slots.Count == 0
                ? "none"
                : string.Join(", ", e.Slots.Select(s => $"{s.Position}:{(s.WasKept ? "kept" : "new")}"));
            Trace.WriteLine($"[HSIntel][Opponent] Mulligan slots => {slotSummary} (coinKept={e.CoinKept})");

            foreach(var slot in e.Slots)
            {
                var origins = new List<CardOriginTag>
                {
                    new CardOriginTag(slot.WasKept ? CardOriginType.KeptInMulligan : CardOriginType.OriginalDeck)
                };

                HandCardIdentity? identity = null;
                if(slot.IsKnown && !string.IsNullOrWhiteSpace(slot.CardId))
                    identity = new HandCardIdentity(slot.EntityId, slot.CardId, null);

                slots.Add(new OpponentHandSlot(
                    slot.Position,
                    identity,
                    slot.WasKept,
                    origins,
                    Array.Empty<CardCandidate>(),
                    e.Timestamp));
            }

            lock(_syncRoot)
            {
                _model = _model.With(
                    handSlots: slots,
                    secretCandidates: _model.SecretCandidates,
                    dataQuality: _model.DataQuality,
                    lastUpdated: e.Timestamp);
            }

            OpponentModelUpdated?.Invoke(this, new OpponentModelUpdatedEventArgs(CurrentModel, e.Timestamp));
        }

        private void HandleOpponentCardCreated(object? sender, OpponentCardCreatedEventArgs e)
        {
            var updatedSlot = UpdateSlot(
                e.Position,
                slot =>
                {
                    var origins = MergeOrigins(slot.Origins, e.Origin, e.CreatedByCardId);

                    IReadOnlyList<CardCandidate> candidates;
                    if(e.CandidateCardIds.Count > 0)
                    {
                        candidates = e.CandidateCardIds
                            .Where(id => !string.IsNullOrWhiteSpace(id))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Select(id => new CardCandidate(id!, null, e.CreatedByCardId))
                            .ToList();
                    }
                    else
                    {
                        candidates = slot.CandidateCards;
                    }

                    var identity = slot.KnownCard;
                    if(!string.IsNullOrWhiteSpace(e.CardId))
                    {
                        identity = new HandCardIdentity(e.EntityId, e.CardId, null);
                    }
                    else if(identity == null && e.EntityId.HasValue)
                    {
                        identity = new HandCardIdentity(e.EntityId, null, null);
                    }

                    return new OpponentHandSlot(
                        slot.Position,
                        identity,
                        slot.WasKeptInMulligan,
                        origins,
                        candidates,
                        e.Timestamp);
                },
                e.Timestamp);

            var candidateSummary = updatedSlot.CandidateCards.Count == 0
                ? "[]"
                : "[" + string.Join(", ", updatedSlot.CandidateCards.Select(c => c.CardId)) + "]";
            var knownCardId = updatedSlot.KnownCard?.CardId ?? "unknown";
            var creatorId = e.CreatedByCardId ?? "unknown";
            Trace.WriteLine($"[HSIntel][Opponent] Created origin={e.Origin} slot={e.Position} card={knownCardId} candidates={candidateSummary} creator={creatorId}");
        }

        private OpponentHandSlot UpdateSlot(int position, Func<OpponentHandSlot, OpponentHandSlot> updater, DateTimeOffset timestamp)
        {
            OpponentModel updatedModel;
            OpponentHandSlot updatedSlot;
            lock(_syncRoot)
            {
                var slots = _model.HandSlots.ToList();
                while(slots.Count <= position)
                {
                    slots.Add(CreateEmptySlot(slots.Count, timestamp));
                }

                var current = slots[position];
                updatedSlot = updater(current);
                slots[position] = updatedSlot;

                updatedModel = _model.With(
                    handSlots: slots,
                    secretCandidates: _model.SecretCandidates,
                    dataQuality: _model.DataQuality,
                    lastUpdated: timestamp);
                _model = updatedModel;
            }

            OpponentModelUpdated?.Invoke(this, new OpponentModelUpdatedEventArgs(updatedModel, timestamp));
            return updatedSlot;
        }

        private static IReadOnlyList<CardOriginTag> MergeOrigins(
            IReadOnlyList<CardOriginTag> existing,
            CardOriginType originType,
            string? sourceCardId)
        {
            var origins = new List<CardOriginTag>();
            if(existing != null)
                origins.AddRange(existing);

            origins.RemoveAll(o => o.Type == originType);
            origins.Add(new CardOriginTag(originType, sourceCardId, null));
            return origins;
        }

        private static OpponentHandSlot CreateEmptySlot(int position, DateTimeOffset timestamp) =>
            new OpponentHandSlot(
                position,
                null,
                false,
                Array.Empty<CardOriginTag>(),
                Array.Empty<CardCandidate>(),
                timestamp);

        private void HandleOpponentSecretsChanged(object? sender, OpponentSecretsChangedEventArgs e)
        {
            if(e == null)
                return;

            var newGroups = (e.Groups ?? Array.Empty<SecretCandidateGroup>())
                .Select((group, index) => CloneGroup(group, e.Timestamp))
                .ToList();

            var previousGroups = CurrentModel.SecretCandidates;
            var previousMap = BuildGroupMap(previousGroups);
            var currentMap = BuildGroupMap(newGroups);

            var additions = new List<SecretCandidateGroup>();
            var removals = new List<SecretCandidateGroup>();
            var eliminations = new List<(SecretCandidateGroup group, string cardId)>();

            foreach(var kvp in currentMap)
            {
                if(!previousMap.TryGetValue(kvp.Key, out var previous))
                {
                    additions.Add(kvp.Value);
                    continue;
                }

                var removedCards = GetRemovedCandidateIds(previous, kvp.Value);
                foreach(var removed in removedCards)
                    eliminations.Add((kvp.Value, removed));
            }

            foreach(var kvp in previousMap)
            {
                if(!currentMap.ContainsKey(kvp.Key))
                    removals.Add(kvp.Value);
            }

            var reason = DescribeSecretCause(e.Cause);

            foreach(var addition in additions)
                LogSecretAdded(addition);

            foreach(var elimination in eliminations)
                LogSecretEliminated(elimination.group, elimination.cardId, reason);

            foreach(var removal in removals)
                LogSecretResolved(removal, reason);

            LogSecretSummary(newGroups);

            lock(_syncRoot)
            {
                _model = _model.With(
                    handSlots: _model.HandSlots,
                    secretCandidates: newGroups,
                    dataQuality: _model.DataQuality,
                    lastUpdated: e.Timestamp);
            }

            OpponentModelUpdated?.Invoke(this, new OpponentModelUpdatedEventArgs(CurrentModel, e.Timestamp));
        }

        private static SecretCandidateGroup CloneGroup(SecretCandidateGroup group, DateTimeOffset timestamp)
        {
            var candidates = group.Candidates
                .Select(c => new SecretCandidate(c.CardId, c.Probability, c.Status))
                .ToList();

            return new SecretCandidateGroup(group.CardClass, candidates, timestamp, group.EntityId);
        }

        private static IReadOnlyDictionary<string, SecretCandidateGroup> BuildGroupMap(
            IReadOnlyList<SecretCandidateGroup> groups)
        {
            var map = new Dictionary<string, SecretCandidateGroup>();
            if(groups == null || groups.Count == 0)
                return map;

            for(var index = 0; index < groups.Count; index++)
            {
                var group = groups[index];
                var key = BuildGroupKey(group, index);
                if(!map.ContainsKey(key))
                    map[key] = group;
            }

            return map;
        }

        private static string BuildGroupKey(SecretCandidateGroup group, int index)
        {
            if(group.EntityId.HasValue)
                return $"entity:{group.EntityId.Value}";

            var cardClass = group.CardClass ?? "Unknown";
            return $"class:{cardClass}:{index}";
        }

        private static IReadOnlyList<string> GetRemovedCandidateIds(
            SecretCandidateGroup previous,
            SecretCandidateGroup current)
        {
            var previousIds = new HashSet<string>(
                previous.Candidates
                    .Select(c => c.CardId)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id!),
                StringComparer.OrdinalIgnoreCase);

            var currentIds = new HashSet<string>(
                current.Candidates
                    .Select(c => c.CardId)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id!),
                StringComparer.OrdinalIgnoreCase);

            previousIds.ExceptWith(currentIds);
            if(previousIds.Count == 0)
                return Array.Empty<string>();

            return previousIds.ToList();
        }

        private static string DescribeSecretCause(string? cause)
        {
            if(string.IsNullOrWhiteSpace(cause))
                return "Unknown";

            return cause switch
            {
                "DirectAttack" => "DirectAttack",
                "FriendlyCardPlayed" => "FriendlyCardPlayed",
                "FriendlyCardDiscarded" => "FriendlyCardDiscarded",
                "FriendlyHeroPower" => "FriendlyHeroPower",
                "FriendlyTurnStart" => "FriendlyTurnStart",
                "OpponentTurnStart" => "OpponentTurnStart",
                "GameStarted" => "GameStarted",
                "GameEnded" => "GameEnded",
                "OpponentCardPlayed" => "OpponentCardPlayed",
                _ => cause!
            };
        }

        private static void LogSecretAdded(SecretCandidateGroup group)
        {
            var classLabel = group.CardClass ?? "Unknown";
            var candidates = group.Candidates.Count == 0
                ? "[]"
                : "[" + string.Join(", ", group.Candidates.Select(c => c.CardId)) + "]";
            Trace.WriteLine($"[HSIntel][Opponent] Secret added class={classLabel} candidates={candidates}");
        }

        private static void LogSecretEliminated(SecretCandidateGroup group, string cardId, string reason)
        {
            var classLabel = group.CardClass ?? "Unknown";
            Trace.WriteLine($"[HSIntel][Opponent] Secret eliminated={cardId} class={classLabel} reason={reason}");
        }

        private static void LogSecretResolved(SecretCandidateGroup group, string reason)
        {
            var classLabel = group.CardClass ?? "Unknown";
            Trace.WriteLine($"[HSIntel][Opponent] Secret resolved class={classLabel} reason={reason}");
        }

        private static void LogSecretSummary(IReadOnlyList<SecretCandidateGroup> groups)
        {
            var summary = groups == null || groups.Count == 0
                ? "none"
                : string.Join(" | ", groups.Select(g =>
                {
                    var classLabel = g.CardClass ?? "Unknown";
                    var candidates = g.Candidates.Count == 0
                        ? "[]"
                        : "[" + string.Join(", ", g.Candidates.Select(c => c.CardId)) + "]";
                    return $"{classLabel}:{candidates}";
                }));

            Trace.WriteLine($"[HSIntel][Opponent] Secret set => remaining={summary}");
        }

        private void HandleOpponentHandOddsEvaluated(object? sender, OpponentHandOddsEventArgs e)
        {
            if(e == null)
                return;

            var probability = e.SingleCopyProbability;
            var shouldUpdate = probability.HasValue;

            var existingSlots = CurrentModel.HandSlots;
            if(existingSlots == null || existingSlots.Count == 0)
            {
                LogHandOdds(probability, e);
                return;
            }

            var updatedSlots = new List<OpponentHandSlot>(existingSlots.Count);
            var changed = false;

            foreach(var slot in existingSlots)
            {
                if(slot.CandidateCards.Count == 0 || !shouldUpdate)
                {
                    updatedSlots.Add(slot);
                    continue;
                }

                var updatedCandidates = slot.CandidateCards
                    .Select(c => new CardCandidate(c.CardId, probability, c.Note))
                    .ToList();

                if(!AreCandidateListsEqual(slot.CandidateCards, updatedCandidates))
                {
                    changed = true;
                    updatedSlots.Add(slot.With(candidateCards: updatedCandidates, lastUpdated: e.Timestamp));
                }
                else
                {
                    updatedSlots.Add(slot);
                }
            }

            if(changed)
            {
                lock(_syncRoot)
                {
                    _model = _model.With(
                        handSlots: updatedSlots,
                        secretCandidates: _model.SecretCandidates,
                        dataQuality: _model.DataQuality,
                        lastUpdated: e.Timestamp);
                }

                OpponentModelUpdated?.Invoke(this, new OpponentModelUpdatedEventArgs(CurrentModel, e.Timestamp));
            }

            LogHandOdds(probability, e);
        }

        private static bool AreCandidateListsEqual(
            IReadOnlyList<CardCandidate> existing,
            IReadOnlyList<CardCandidate> updated)
        {
            if(existing.Count != updated.Count)
                return false;

            for(var i = 0; i < existing.Count; i++)
            {
                var current = existing[i];
                var next = updated[i];

                if(!string.Equals(current.CardId, next.CardId, StringComparison.OrdinalIgnoreCase))
                    return false;

                if(current.Probability.HasValue != next.Probability.HasValue)
                    return false;

                if(current.Probability.HasValue &&
                   next.Probability.HasValue &&
                   Math.Abs(current.Probability.Value - next.Probability.Value) > 0.0001)
                    return false;
            }

            return true;
        }

        private static void LogHandOdds(double? probability, OpponentHandOddsEventArgs context)
        {
            var probabilityText = probability.HasValue
                ? probability.Value.ToString("0.000")
                : "n/a";
            Trace.WriteLine(
                $"[HSIntel][Opponent] Hand odds => p(single)={probabilityText} deck={context.DeckCount} hand={context.HandCount} coin={context.HasCoin} activeSide={context.ActiveSide}");
        }
    }
}
