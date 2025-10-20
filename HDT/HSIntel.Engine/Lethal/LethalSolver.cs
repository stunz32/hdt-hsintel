using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HSIntel.Core.Config;
using HSIntel.Core.Models;
using HSIntel.Core.Models.Events;
using HSIntel.Engine.Models;
using HSIntel.Engine.Data;
using HSIntel.Engine.Internal;

namespace HSIntel.Engine.Lethal
{
    /// <summary>
    /// Fast-path lethal finder with minimal risk changes.
    /// Base: minions + taunt trades. Extended: curated direct damage, hero swings, hero power.
    /// </summary>
    public sealed class LethalSolver
    {

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
                AttacksRemaining = src.EntityId.HasValue ? HdtEntityCombat.GetAttacksRemaining(src.EntityId.Value) : 1;
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

        public LethalResult? FindLethal(GameContext context, HSIntelConfig config)
        {
            if(context == null) throw new ArgumentNullException(nameof(context));
            if(config == null) throw new ArgumentNullException(nameof(config));

            var sw = Stopwatch.StartNew();
            var timeBudgetMs = Math.Max(5, Math.Min(10, config.MaxComputeMs / 25));

            if(context.ActiveSide != ParticipantSide.Friendly || context.DataQuality == DataQuality.Degraded)
                return null;

            var board = context.Board;
            var oppHero = board.OpponentHero;
            if(oppHero == null)
                return null;

            var oppEffectiveHealth = Math.Max(0, (oppHero.Health ?? 0) + (oppHero.Armor ?? 0));
            if(oppEffectiveHealth <= 0)
                return new LethalResult(Array.Empty<GameAction>(), 0, 0);

            var friendly = board.FriendlyMinions
                .Select(m => new SimMinion(m))
                .Where(m => m.Alive && !m.IsFrozen && m.Attack > 0 && (m.Source.EntityId.HasValue ? HdtEntityCombat.GetAttacksRemaining(m.Source.EntityId.Value) > 0 : true))
                .ToList();

            var taunts = board.OpponentMinions
                .Where(m => m.HasTaunt)
                .Select(m => new SimMinion(m))
                .ToList();

            var planned = new List<GameAction>();

            // Available mana snapshot
            int manaAvailable = Math.Max(0, context.FriendlyMana.Available ?? context.FriendlyMana.Total ?? 0);

            // Curated direct-damage spells plan (face-flex-aoe only)
            var spellPlan = PlanDirectDamageSpells(context, manaAvailable);
            int spellDamage = spellPlan.TotalDamage;
            int manaAfterSpells = Math.Max(0, manaAvailable - spellPlan.ManaSpent);

            // Curated hero power plan
            var heroPowerPlan = PlanHeroPower(context, manaAfterSpells);
            int heroPowerDamage = heroPowerPlan.Damage;
            int manaAfterHeroPower = Math.Max(0, manaAfterSpells - heroPowerPlan.ManaCost);

            // Potential hero swing to face (only if no taunts)
            var heroSwing = PlanHeroSwing(board);

            if(taunts.Count == 0)
            {
                var faceEligible = friendly.Where(m => m.Source.EntityId.HasValue ? HdtEntityCombat.CanAttackFaceNow(m.Source.EntityId.Value) : true).ToList();
                var faceDamage = faceEligible.Sum(m => m.Attack * Math.Max(1, m.AttacksRemaining));
                var total = faceDamage + spellDamage + heroPowerDamage + heroSwing.Damage;
                if(faceDamage >= oppEffectiveHealth)
                {
                    // Minions alone suffice
                    planned.AddRange(BuildFaceActionsOrdered(faceEligible.SelectMany(m => Repeat(m.Source, Math.Max(1, m.AttacksRemaining)))));
                    return new LethalResult(planned, faceDamage, requiredMana: 0);
                }
                if(total >= oppEffectiveHealth)
                {
                    // Order: small spells first, hero swing, minion swings, hero power last
                    planned.AddRange(spellPlan.ActionsToFace);
                    if(heroSwing.Action != null)
                        planned.Add(heroSwing.Action);
                    var expanded = faceEligible.OrderByDescending(a => a.Attack).SelectMany(a => Repeat(a.Source, Math.Max(1, a.AttacksRemaining)));
                    planned.AddRange(BuildFaceActionsOrdered(expanded));
                    if(heroPowerPlan.Action != null)
                        planned.Add(heroPowerPlan.Action);
                    return new LethalResult(planned, total, requiredMana: spellPlan.ManaSpent + heroPowerPlan.ManaCost);
                }
                // No deterministic lethal
                MaybeEmitRngProbability(context, config, oppEffectiveHealth - total, timeBudgetMs);
                return null;
            }

            // Greedy trades to clear taunts
            while(taunts.Any(t => t.Alive) && sw.ElapsedMilliseconds < timeBudgetMs)
            {
                var attacker = friendly.Where(a => a.Alive && a.AttacksRemaining > 0 && (a.Source.EntityId.HasValue ? HdtEntityCombat.CanAttackMinionsNow(a.Source.EntityId.Value) : true)).OrderByDescending(a => a.Attack).FirstOrDefault();
                if(attacker == null)
                    return null;

                var shielded = taunts.Where(t => t.Alive && t.HasDivineShield).OrderByDescending(t => t.Health).FirstOrDefault();
                if(shielded != null)
                {
                    var popper = friendly.Where(a => a.Alive && a.AttacksRemaining > 0 && (a.Source.EntityId.HasValue ? HdtEntityCombat.CanAttackMinionsNow(a.Source.EntityId.Value) : true)).OrderBy(a => a.Attack).First();
                    PlanAndApplyTrade(popper, shielded, planned);
                    continue;
                }

                var kill = (from t in taunts.Where(t => t.Alive && !t.HasDivineShield)
                                     from a in friendly.Where(a => a.Alive && a.AttacksRemaining > 0 && (a.Source.EntityId.HasValue ? HdtEntityCombat.CanAttackMinionsNow(a.Source.EntityId.Value) : true))
                            where a.Attack >= t.Health
                            orderby a.Attack ascending, t.Health descending
                            select (a, t)).FirstOrDefault();
                if(kill.a != null && kill.t != null)
                {
                    PlanAndApplyTrade(kill.a, kill.t, planned);
                    continue;
                }

                var hardest = taunts.Where(t => t.Alive).OrderByDescending(t => t.Health).ThenByDescending(t => t.Attack).First();
                PlanAndApplyTrade(attacker, hardest, planned);
            }

            if(taunts.Any(t => t.Alive))
                return null;

            var remaining = friendly.Where(a => a.Alive && a.AttacksRemaining > 0 && (a.Source.EntityId.HasValue ? HdtEntityCombat.CanAttackFaceNow(a.Source.EntityId.Value) : true)).ToList();
            var potentialFace = remaining.Sum(a => a.Attack);

            // After taunts are cleared, we can include spells, hero swing, and hero power
            var totalAfterTrades = potentialFace + spellDamage + heroPowerDamage + heroSwing.Damage;
            if(totalAfterTrades < oppEffectiveHealth)
            {
                MaybeEmitRngProbability(context, config, oppEffectiveHealth - totalAfterTrades, timeBudgetMs);
                return null;
            }

            // Order: spells to face first, then hero swing, then minions, then hero power
            planned.AddRange(spellPlan.ActionsToFace);
            if(heroSwing.Action != null)
                planned.Add(heroSwing.Action);
            planned.AddRange(BuildFaceActionsOrdered(remaining.OrderByDescending(a => a.Attack).Select(a => a.Source)));
            if(heroPowerPlan.Action != null)
                planned.Add(heroPowerPlan.Action);
            return new LethalResult(planned, totalAfterTrades, requiredMana: spellPlan.ManaSpent + heroPowerPlan.ManaCost);
        }

        private static void PlanAndApplyTrade(SimMinion attacker, SimMinion target, List<GameAction> actions)
        {
            var actionTarget = new ActionTarget(ParticipantSide.Opponent, target.Source.EntityId, false, target.Source.CardId, target.Source.CardName);
            var attackerName = string.IsNullOrWhiteSpace(attacker.Source.CardName) ? attacker.Source.CardId ?? $"Minion#{attacker.Source.EntityId?.ToString() ?? "?"}" : attacker.Source.CardName!;
            var targetName = string.IsNullOrWhiteSpace(target.Source.CardName) ? target.Source.CardId ?? $"Minion#{target.Source.EntityId?.ToString() ?? "?"}" : target.Source.CardName!;
            var description = $"Attack with {attackerName} -> {targetName}";
            actions.Add(new GameAction(GameActionType.Attack, description, priority: 999, card: null, attacker: attacker.Source, target: actionTarget, requiresTarget: true));

            var dmgToTarget = attacker.Attack;
            var dmgToAttacker = target.Attack;

            if(target.HasDivineShield) { target.HasDivineShield = false; dmgToTarget = 0; }
            if(attacker.HasDivineShield) { attacker.HasDivineShield = false; dmgToAttacker = 0; }

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

        private static (int Damage, GameAction? Action) PlanHeroSwing(BoardState board)
        {
            var hero = board.FriendlyHero;
            if(hero == null || hero.IsFrozen)
                return (0, null);
            var w = hero.Weapon;
            if(w == null || (w.Attack ?? 0) <= 0 || (w.Durability ?? 0) <= 0)
                return (0, null);
            // Only valid if attacking face is legal (no taunts handled by caller)
            var dmg = Math.Max(0, w.Attack ?? 0);
            var target = new ActionTarget(ParticipantSide.Opponent, null, true, null, "Hero");
            var action = new GameAction(GameActionType.HeroAttack, "Hero attack -> Opponent Hero", priority: 850, card: null, attacker: null, target: target, requiresTarget: true, heroAttacker: hero);
            return (dmg, action);
        }

        private sealed class SpellPlan
        {
            public List<GameAction> ActionsToFace { get; } = new List<GameAction>();
            public int TotalDamage { get; set; }
            public int ManaSpent { get; set; }
        }

        private static SpellPlan PlanDirectDamageSpells(GameContext context, int manaAvailable)
        {
            var plan = new SpellPlan();
            if(!EngineToggles.EnableCuratedSpells)
                return plan;

            var hand = context.FriendlyHand.Cards;
            if(hand.Count == 0)
                return plan;

            var candidates = new List<(HandCardState card, DirectDamageInfo info)>();
            foreach(var c in hand)
            {
                if(DirectDamageProvider.TryGet(c.CardId, out var info))
                {
                    if(info.Target == DamageTargetKind.Face || info.Target == DamageTargetKind.Flex || info.Target == DamageTargetKind.Aoe)
                        candidates.Add((c, info));
                }
            }

            if(candidates.Count == 0)
                return plan;

            // Greedy by damage desc, then mana asc
            foreach(var entry in candidates.OrderByDescending(e => e.info.Damage).ThenBy(e => e.info.ManaCost))
            {
                if(plan.ManaSpent + entry.info.ManaCost > manaAvailable)
                    continue;
                // Build action to face
                var target = new ActionTarget(ParticipantSide.Opponent, null, isHero: true, cardId: null, cardName: "Hero");
                var name = string.IsNullOrWhiteSpace(entry.card.CardName) ? (entry.card.CardId ?? "Spell") : entry.card.CardName!;
                var action = new GameAction(GameActionType.PlayCard, $"Play {name}", priority: 950, card: entry.card, attacker: null, target: target, requiresTarget: (entry.info.Target != DamageTargetKind.Aoe));
                plan.ActionsToFace.Add(action);
                plan.TotalDamage += Math.Max(0, entry.info.Damage);
                plan.ManaSpent += Math.Max(0, entry.info.ManaCost);
            }

            return plan;
        }

        private sealed class HeroPowerPlan
        {
            public GameAction? Action { get; set; }
            public int Damage { get; set; }
            public int ManaCost { get; set; }
        }

        private static HeroPowerPlan PlanHeroPower(GameContext context, int manaAvailable)
        {
            var plan = new HeroPowerPlan();
            if(!EngineToggles.EnableCuratedHeroPowerDamage)
                return plan;

            if(manaAvailable < 2)
                return plan;

            var hero = context.Board.FriendlyHero;
            if(hero == null)
                return plan;

            if(!(HeroPowerProvider.TryGetByCardId(hero.HeroPowerCardId, out var hp) || HeroPowerProvider.TryGetByClass(hero.HeroClass, out hp)))
                return plan;

            // Build action (target face for flex/face)
            var target = new ActionTarget(ParticipantSide.Opponent, null, true, null, "Hero");
            var action = new GameAction(GameActionType.HeroPower, "Use Hero Power", priority: 500, card: null, attacker: null, target: target, requiresTarget: false);
            plan.Action = action;
            plan.Damage = Math.Max(0, hp.Damage);
            plan.ManaCost = 2;
            return plan;
        }

        private static void MaybeEmitRngProbability(GameContext context, HSIntelConfig config, int damageShortfall, int timeBudgetMs)
        {
            try
            {
                if(!config.EnableRollouts || config.RolloutSamples <= 0)
                    return; // Coordinator will log the skipped line

                // Arcane Missiles scenario: when shortfall <= 3 and card present in hand
                var hand = context.FriendlyHand.Cards;
                foreach(var c in hand)
                {
                    if(c.CardId != null && RandomResolverRegistry.TryGet(c.CardId, out var resolver))
                    {
                        // Rough trigger condition
                        if(damageShortfall <= 3)
                        {
                            var budget = Math.Max(10, Math.Min(50, config.MaxComputeMs / 5));
                            var lp = resolver.ComputeLethalProbability(context, config.RolloutSamples, budget);
                            Trace.WriteLine($"[HSIntel][Engine] RNG: lethal probability {lp.Probability * 100f:F0}% (n={lp.SamplesRun})");
                            break;
                        }
                    }
                }
            }
            catch { }
        }
    }
}
