using System;
using System.Collections.Generic;
using System.Linq;
using HSIntel.Core.Models;
using HSIntel.Core.Models.Events;
using HSIntel.Engine.Models;

namespace HSIntel.Engine.Search
{
    internal sealed class TranspositionTable
    {
        private sealed class Entry
        {
            public Entry(ulong hash, int depth, double score)
            {
                Hash = hash;
                Depth = depth;
                Score = score;
            }

            public ulong Hash { get; }

            public int Depth { get; private set; }

            public double Score { get; private set; }

            public void Update(int depth, double score)
            {
                Depth = depth;
                Score = score;
            }
        }

        private readonly Dictionary<ulong, Entry> _entries = new Dictionary<ulong, Entry>();

        private int _probes;
        private int _hits;
        private int _stores;
        private int _evictions;

        public ulong ComputeHash(GameContext context, SimulationMetrics metrics)
        {
            var hash = InitializeHash();
            hash = Mix(hash, context.ActiveSide.HasValue ? (int)context.ActiveSide.Value : -1);
            hash = Mix(hash, context.TurnNumber ?? -1);
            hash = Mix(hash, (int)context.DataQuality);

            hash = HashHero(hash, context.Board.FriendlyHero);
            hash = HashHero(hash, context.Board.OpponentHero);

            foreach(var minion in context.Board.FriendlyMinions)
                hash = HashMinion(hash, minion);
            foreach(var minion in context.Board.OpponentMinions)
                hash = HashMinion(hash, minion);
            foreach(var secret in context.Board.Secrets)
                hash = HashSecret(hash, secret);

            hash = HashHand(hash, context.FriendlyHand);
            hash = HashHand(hash, context.OpponentHand);

            hash = HashMana(hash, context.FriendlyMana);
            hash = HashMana(hash, context.OpponentMana);

            hash = Mix(hash, metrics.FriendlyCardsPlayed);
            hash = Mix(hash, metrics.FriendlyAttacks);
            hash = Mix(hash, metrics.FriendlyManaSpent);
            hash = Mix(hash, metrics.FriendlyHeroPowerUses);
            hash = Mix(hash, metrics.FriendlyMinionsRemoved);
            hash = Mix(hash, metrics.OpponentMinionsRemoved);
            hash = Mix(hash, metrics.DamageDealtToOpponentHero);
            hash = Mix(hash, metrics.DamageTakenByFriendlyHero);

            hash = Mix(hash, context.GameId ?? string.Empty);

            return hash;
        }

        public bool TryGet(ulong hash, int depth, out double score)
        {
            _probes++;
            if(_entries.TryGetValue(hash, out var entry))
            {
                if(entry.Depth >= depth)
                {
                    _hits++;
                    score = entry.Score;
                    return true;
                }
            }

            score = 0;
            return false;
        }

        public void Store(ulong hash, int depth, double score)
        {
            if(_entries.TryGetValue(hash, out var entry))
            {
                if(depth >= entry.Depth && score > entry.Score)
                    entry.Update(depth, score);
                return;
            }

            _entries[hash] = new Entry(hash, depth, score);
            _stores++;
        }

        public void Prune(int maxSize)
        {
            if(maxSize <= 0 || _entries.Count <= maxSize)
                return;

            var survivors = _entries.Values
                .OrderByDescending(e => e.Depth)
                .ThenByDescending(e => e.Score)
                .Take(maxSize)
                .ToDictionary(e => e.Hash);

            _evictions += _entries.Count - survivors.Count;
            _entries.Clear();
            foreach(var kvp in survivors)
                _entries[kvp.Key] = kvp.Value;
        }

        public TranspositionStats Snapshot() =>
            new TranspositionStats(_probes, _hits, _stores, _evictions, _entries.Count);

        private static ulong InitializeHash() => 1469598103934665603UL;

        private static ulong Mix(ulong hash, int value)
        {
            unchecked
            {
                return (hash ^ (ulong)(uint)value) * 1099511628211UL;
            }
        }

        private static ulong Mix(ulong hash, bool value) => Mix(hash, value ? 1 : 0);

        private static ulong Mix(ulong hash, string value)
        {
            var strHash = StringComparer.Ordinal.GetHashCode(value);
            return Mix(hash, strHash);
        }

        private static ulong HashHero(ulong hash, HeroState hero)
        {
            hash = Mix(hash, hero.Health ?? 0);
            hash = Mix(hash, hero.Armor ?? 0);
            hash = Mix(hash, hero.IsImmune);
            hash = Mix(hash, hero.IsFrozen);

            if(hero.Weapon != null)
            {
                hash = Mix(hash, hero.Weapon.Attack ?? 0);
                hash = Mix(hash, hero.Weapon.Durability ?? 0);
                hash = Mix(hash, hero.Weapon.IsPoisonous);
                hash = Mix(hash, hero.Weapon.HasWindfury);
                hash = Mix(hash, hero.Weapon.CardId ?? string.Empty);
            }

            return hash;
        }

        private static ulong HashMinion(ulong hash, MinionState minion)
        {
            hash = Mix(hash, minion.Attack ?? 0);
            hash = Mix(hash, minion.Health ?? 0);
            hash = Mix(hash, minion.Position);
            hash = Mix(hash, minion.HasDivineShield);
            hash = Mix(hash, minion.HasTaunt);
            hash = Mix(hash, minion.IsStealthed);
            hash = Mix(hash, minion.IsFrozen);
            hash = Mix(hash, minion.IsDormant);
            hash = Mix(hash, (int)minion.Controller);
            hash = Mix(hash, minion.CardId ?? string.Empty);
            return hash;
        }

        private static ulong HashSecret(ulong hash, SecretState secret)
        {
            hash = Mix(hash, secret.TurnsInPlay);
            hash = Mix(hash, secret.WasCreated);
            hash = Mix(hash, (int)secret.Controller);
            hash = Mix(hash, secret.CardId ?? string.Empty);
            return hash;
        }

        private static ulong HashHand(ulong hash, HandState hand)
        {
            hash = Mix(hash, hand.TotalCardCount ?? hand.Cards.Count);
            foreach(var card in hand.Cards)
            {
                hash = Mix(hash, card.EffectiveCost ?? card.BaseCost ?? 0);
                hash = Mix(hash, card.IsKnown);
                hash = Mix(hash, card.IsPlayable);
                hash = Mix(hash, card.CardId ?? string.Empty);
            }

            return hash;
        }

        private static ulong HashMana(ulong hash, ManaState mana)
        {
            hash = Mix(hash, mana.Total ?? 0);
            hash = Mix(hash, mana.Available ?? 0);
            hash = Mix(hash, mana.Overloaded ?? 0);
            hash = Mix(hash, mana.Locked ?? 0);
            hash = Mix(hash, mana.Temporary ?? 0);
            return hash;
        }
    }
}
