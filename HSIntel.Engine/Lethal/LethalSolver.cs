using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HSIntel.Core.Config;
using HSIntel.Core.Models;
using HSIntel.Engine.Models;
using HSIntel.Core.Models.Events;

namespace HSIntel.Engine.Lethal
{
    /// <summary>
    /// Minimal, fast-path lethal detector using board minions only.
    /// - Excludes hero attacks and spells.
    /// - Respects taunts by planning necessary trades first.
    /// - Aims to be deterministic and fast (&lt;~1-2ms typical).
    /// </summary>
    internal sealed class LethalSolver
    {
        internal sealed class LethalPlan
        {
            public LethalPlan(IReadOnlyList<GameAction> actions, int totalDamage, int manaSpent)
            {
                Actions = actions ?? Array.Empty<GameAction>();
                TotalDamage = totalDamage;
                ManaSpent = manaSpent;
            }

            public IReadOnlyList<GameAction> Actions { get; }
            public int TotalDamage { get; }
            public int ManaSpent { get; }
        }

        private sealed class SimMinion
        {
            public SimMinion(MinionState src)
            {
                Source = src;
                Attack = Math.Max(0, src.Attack ?? 0);
                Health = Math.Max(0, src.Health ?? 0);
                HasDivineShield = src.HasDivineShield;
                IsFrozen = src.IsFrozen;
                IsDormant = src.IsDormant;
                AttacksRemaining = src.EntityId.HasValue ? HSIntel.Engine.Internal.HdtEntityCombat.GetAttacksRemaining(src.EntityId.Value) : 1;
                Alive = Health > 0 && !IsDormant;
            }

            public MinionState Source { get; }
            public int Attack { get; }
            public int Health { get; set; }
            public bool HasDivineShield { get; set; }
            public bool IsFrozen { get; }
            public bool IsDormant { get; }
            public int AttacksRemaining { get; set; }
            public bool Alive { get; set; }
        }

        /// <summary>
        /// Attempts to find a board-only lethal plan. Returns null if not found or not confidently feasible.
        /// </summary>
        public LethalPlan? TryFindLethal(GameContext context, HSIntelConfig config)
        {
            if(context == null) throw new ArgumentNullException(nameof(context));
            if(config == null) throw new ArgumentNullException(nameof(config));

            var sw = Stopwatch.StartNew();
            var timeBudgetMs = Math.Max(5, Math.Min(10, config.MaxComputeMs / 25)); // stay tiny vs beam search

            // Only consider friendly turns with normal data quality
            if(context.ActiveSide != ParticipantSide.Friendly || context.DataQuality == DataQuality.Degraded)
                return null;

            var board = context.Board;
            if(board == null) return null;

            var opponentHero = board.OpponentHero;
            if(opponentHero == null) return null;

            var oppEffectiveHealth = Math.Max(0, (opponentHero.Health ?? 0) + (opponentHero.Armor ?? 0));
            if(oppEffectiveHealth <= 0)
                return new LethalPlan(Array.Empty<GameAction>(), 0, 0);

            // Prepare sim state
            var friendly = board.FriendlyMinions
                .Select(m => new SimMinion(m))
                .Where(m => m.Alive && !m.IsFrozen && m.Attack > 0 && (m.Source.EntityId.HasValue ? HSIntel.Engine.Internal.HdtEntityCombat.GetAttacksRemaining(m.Source.EntityId.Value) > 0 : true))
                .ToList();

            if(friendly.Count == 0)
                return null;

            var taunts = board.OpponentMinions
                .Where(m => m.HasTaunt)
                .Select(m => new SimMinion(m))
                .ToList();

            // If no taunts, fast check: sum of friendly attack to face
            if(taunts.Count == 0)
            {
                var faceEligible = friendly.Where(m => m.Source.EntityId.HasValue ? HSIntel.Engine.Internal.HdtEntityCombat.CanAttackFaceNow(m.Source.EntityId.Value) : true)
                                           .ToList();
                var faceDamage = faceEligible.Sum(m => m.Attack * Math.Max(1, m.AttacksRemaining));
                if(faceDamage >= oppEffectiveHealth)
                {
                    var actions = BuildFaceActionsOrdered(faceEligible.SelectMany(m => Repeat(m.Source, Math.Max(1, m.AttacksRemaining))));
                    return new LethalPlan(actions, faceDamage, 0);
                }
                return null;
            }

            // Plan minimal trades to clear taunts
            var plannedActions = new List<GameAction>();
            var rng = new Random(1337); // deterministic enough for tie-breaks

            // Greedy: pop divine shields first with lowest-attack unused attacker, then one-shot with tightest fit, else chip with highest attack.
            while(taunts.Any(t => t.Alive) && sw.ElapsedMilliseconds < timeBudgetMs)
            {
                // Get next attacker: prefer unused, alive, highest attack
                var attacker = friendly
                    .Where(a => a.Alive && a.AttacksRemaining > 0 && (a.Source.EntityId.HasValue ? HSIntel.Engine.Internal.HdtEntityCombat.CanAttackMinionsNow(a.Source.EntityId.Value) : true))
                    .OrderByDescending(a => a.Attack)
                    .FirstOrDefault();

                if(attacker == null)
                    return null; // out of attacks before clearing taunts

                // Priority 1: remove a divine shield from a taunt using the lowest-attack available attacker
                var targetWithShield = taunts.Where(t => t.Alive && t.HasDivineShield).OrderByDescending(t => t.Health).FirstOrDefault();
                if(targetWithShield != null)
                {
                    var popper = friendly.Where(a => a.Alive && !a.UsedAttack).OrderBy(a => a.Attack).First();
                    PlanAndApplyTrade(popper, targetWithShield, plannedActions);
                    continue;
                }

                // Priority 2: find a one-shot kill with minimal overkill
                var killCandidate = (from t in taunts.Where(t => t.Alive && !t.HasDivineShield)
                                     from a in friendly.Where(a => a.Alive && a.AttacksRemaining > 0 && (a.Source.EntityId.HasValue ? HSIntel.Engine.Internal.HdtEntityCombat.CanAttackMinionsNow(a.Source.EntityId.Value) : true))
                                     where a.Attack >= t.Health
                                     orderby a.Attack ascending, t.Health descending
                                     select (a, t)).FirstOrDefault();

                if(killCandidate.a != null && killCandidate.t != null)
                {
                    PlanAndApplyTrade(killCandidate.a, killCandidate.t, plannedActions);
                    continue;
                }

                // Priority 3: chip the highest-health taunt with the highest-attack attacker
                var hardestTaunt = taunts.Where(t => t.Alive).OrderByDescending(t => t.Health).ThenByDescending(t => t.Attack).First();
                PlanAndApplyTrade(attacker, hardestTaunt, plannedActions);
            }

            if(taunts.Any(t => t.Alive))
                return null; // time budget exceeded before clearing taunts

            // Remaining face damage comes from unused, alive attackers only (one swing each)
            var remainingFaceAttackers = friendly.Where(a => a.Alive && a.AttacksRemaining > 0 && (a.Source.EntityId.HasValue ? HSIntel.Engine.Internal.HdtEntityCombat.CanAttackFaceNow(a.Source.EntityId.Value) : true)).ToList();
            var potentialFaceDamage = remainingFaceAttackers.Sum(a => a.Attack * Math.Max(1, a.AttacksRemaining));
            if(potentialFaceDamage < oppEffectiveHealth)
                return null;

            // Build face swings ordered by descending attack, expanding per remaining swing
            var expanded = remainingFaceAttackers
                .OrderByDescending(a => a.Attack)
                .SelectMany(a => Repeat(a.Source, Math.Max(1, a.AttacksRemaining)));
            plannedActions.AddRange(BuildFaceActionsOrdered(expanded));

            return new LethalPlan(plannedActions, potentialFaceDamage, 0);
        }

        private static void PlanAndApplyTrade(SimMinion attacker, SimMinion target, List<GameAction> actions)
        {
            if(attacker == null) throw new ArgumentNullException(nameof(attacker));
            if(target == null) throw new ArgumentNullException(nameof(target));

            // Record action
            var actionTarget = new ActionTarget(ParticipantSide.Opponent, target.Source.EntityId, false, target.Source.CardId, target.Source.CardName);
            var attackerName = string.IsNullOrWhiteSpace(attacker.Source.CardName) ? attacker.Source.CardId ?? $"Minion#{attacker.Source.EntityId?.ToString() ?? "?"}" : attacker.Source.CardName!;
            var targetName = string.IsNullOrWhiteSpace(target.Source.CardName) ? target.Source.CardId ?? $"Minion#{target.Source.EntityId?.ToString() ?? "?"}" : target.Source.CardName!;
            var description = $"Attack with {attackerName} -> {targetName}";
            actions.Add(new GameAction(GameActionType.Attack, description, priority: 999, card: null, attacker: attacker.Source, target: actionTarget, requiresTarget: true));

            // Apply simple deterministic combat model (no auras/triggers)
            var dmgToTarget = attacker.Attack;
            var dmgToAttacker = target.Attack;

            if(target.HasDivineShield)
            {
                target.HasDivineShield = false;
                dmgToTarget = 0;
            }

            if(attacker.HasDivineShield)
            {
                attacker.HasDivineShield = false;
                dmgToAttacker = 0;
            }

            target.Health = Math.Max(0, target.Health - dmgToTarget);
            attacker.Health = Math.Max(0, attacker.Health - dmgToAttacker);

            if(target.Health <= 0) target.Alive = false;
            if(attacker.Health <= 0) attacker.Alive = false;

            attacker.AttacksRemaining = Math.Max(0, attacker.AttacksRemaining - 1);
        }

        private static IReadOnlyList<GameAction> BuildFaceActionsOrdered(IEnumerable<MinionState> attackers)
        {
            var actions = new List<GameAction>();
            var targetHero = new ActionTarget(ParticipantSide.Opponent, null, true, null, "Hero");
            foreach(var minion in attackers)
            {
                var name = string.IsNullOrWhiteSpace(minion.CardName) ? minion.CardId ?? $"Minion#{minion.EntityId?.ToString() ?? "?"}" : minion.CardName!;
                var description = $"Attack with {name} -> Opponent Hero";
                actions.Add(new GameAction(GameActionType.Attack, description, priority: 900, card: null, attacker: minion, target: targetHero, requiresTarget: true));
            }

            return actions;
        }

        private static System.Collections.Generic.IEnumerable<T> Repeat<T>(T item, int times)
        {
            for(int i = 0; i < times; i++)
                yield return item;
        }
    }
}
