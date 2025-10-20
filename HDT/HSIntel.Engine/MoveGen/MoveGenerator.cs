using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HSIntel.Core.Models;
using HSIntel.Core.Models.Events;
using HSIntel.Engine.Models;
using HSIntel.Engine.Internal;
using HSIntel.Engine.Data;

namespace HSIntel.Engine.MoveGen
{
    /// <summary>
    /// Generates a heuristic-ordered set of legal actions for the friendly player.
    /// </summary>
    public sealed class MoveGenerator
    {
        public IReadOnlyList<GameAction> Generate(GameContext context)
        {
            if(context == null)
                throw new ArgumentNullException(nameof(context));

            var actions = new List<GameAction>();

            if(context.ActiveSide != ParticipantSide.Friendly)
            {
                actions.Add(CreateEndTurnAction());
                return actions;
            }

            GenerateCardPlays(context, actions);
            GenerateAttacks(context, actions);
            GenerateHeroAttacks(context, actions);
            GenerateHeroPower(context, actions);

            actions.Add(CreateEndTurnAction());

            Func<GameAction, int> orderKey = a =>
            {
                int key = a.Priority;
                if(EngineToggles.EnableSequencingOrdering)
                {
                    // Small spells first (Counterspell test)
                    if(a.ActionType == GameActionType.PlayCard && a.Card != null)
                    {
                        var cost = a.Card.EffectiveCost ?? a.Card.BaseCost ?? 0;
                        key += Math.Min(0, 5 - cost); // prefer lower cost subtly

                        // Information first (draw before playing)
                        if(SequencingProvider.IsDrawFirst(a.Card.CardId))
                            key += 12;

                        // Buffs before summons
                        if(SequencingProvider.IsBuffFirst(a.Card.CardId))
                            key += 8;
                    }
                }
                // Prefer actual plays before hero power when equivalent; improves first-step readability.
                if(EngineToggles.EnableHeroPowerAfterPlaysOrdering)
                {
                    if(a.ActionType == GameActionType.HeroPower)
                        key -= 150; // gentle nudge; still overridden by high-priority heuristics
                }
                if(EngineToggles.EnableSecretRiskOrdering)
                {
                    key += SecretRiskProvider.GetOrderingPenalty(context, a);
                }
                return key;
            };

            var ordered = actions
                .OrderByDescending(orderKey)
                .ThenBy(a => (int)a.ActionType)
                .ToList();

            Trace.WriteLine($"[HSIntel][Engine] MoveGenerator produced {ordered.Count} actions (turn={context.TurnNumber?.ToString() ?? "?"})");
            return ordered;
        }

        private static void GenerateCardPlays(GameContext context, ICollection<GameAction> actions)
        {
            var playableCards = context.FriendlyHand.PlayableCards;
            if(playableCards.Count == 0)
                return;

            var availableMana = context.FriendlyMana.Available ?? context.FriendlyMana.Total ?? 0;
            foreach(var card in playableCards)
            {
                var cost = card.EffectiveCost ?? card.BaseCost ?? 0;
                if(availableMana < cost && cost > 0)
                    continue;

                var name = card.CardName?.Trim();
                var identifier = !string.IsNullOrWhiteSpace(name)
                    ? name
                    : card.CardId ?? "Unknown";

                var priority = 1000 + (cost * 10);
                if(card.WasGeneratedThisTurn)
                    priority += 5;
                if(card.CreatedBy.Count > 0)
                    priority += 2;

                var description = $"Play {identifier}";
                var action = new GameAction(
                    GameActionType.PlayCard,
                    description,
                    priority,
                    card,
                    attacker: null,
                    target: null,
                    requiresTarget: false);

                actions.Add(action);

                // If card is a direct-damage spell we recognize, generate targeted variants
                try
                {
                    if(HSIntel.Engine.Data.DirectDamageProvider.TryGetByIdOrName(card.CardId, card.CardName, out var dd))
                    {
                        var oppHero = context.Board.OpponentHero;
                        var oppMinions = context.Board.OpponentMinions;

                        // Face option when allowed
                        if(dd.Target == HSIntel.Engine.Data.DamageTargetKind.Face || dd.Target == HSIntel.Engine.Data.DamageTargetKind.Flex)
                        {
                            var faceTarget = new ActionTarget(ParticipantSide.Opponent, null, true, null, "Hero");
                            var faceAction = new GameAction(
                                GameActionType.PlayCard,
                                description,
                                priority + 60,
                                card,
                                attacker: null,
                                target: faceTarget,
                                requiresTarget: true);
                            actions.Add(faceAction);
                        }

                        // Minion options: prefer clean kills; fall back to highest attack
                        if(dd.Target == HSIntel.Engine.Data.DamageTargetKind.Minion || dd.Target == HSIntel.Engine.Data.DamageTargetKind.Flex)
                        {
                            var lethalTargets = oppMinions
                                .Where(m => (m.Health ?? 0) <= Math.Max(0, dd.Damage) && !m.IsStealthed)
                                .OrderBy(m => m.Health ?? 0)
                                .ThenByDescending(m => m.Attack ?? 0)
                                .Take(3)
                                .ToList();

                            if(lethalTargets.Count == 0 && oppMinions.Count > 0)
                            {
                                var best = oppMinions
                                    .Where(m => !m.IsStealthed)
                                    .OrderByDescending(m => m.HasTaunt ? 3 : 0)
                                    .ThenByDescending(m => m.Attack ?? 0)
                                    .ThenBy(m => m.Health ?? 0)
                                    .FirstOrDefault();
                                if(best != null)
                                    lethalTargets.Add(best);
                            }

                            foreach(var t in lethalTargets)
                            {
                                var tname = string.IsNullOrWhiteSpace(t.CardName) ? (t.CardId ?? "Minion") : t.CardName!;
                                var tgt = new ActionTarget(ParticipantSide.Opponent, t.EntityId, false, t.CardId, tname);
                                var targeted = new GameAction(
                                    GameActionType.PlayCard,
                                    description,
                                    priority + 80,
                                    card,
                                    attacker: null,
                                    target: tgt,
                                    requiresTarget: true);
                                actions.Add(targeted);
                            }
                        }
                    }
                }
                catch { }
            }
        }

        private static void GenerateAttacks(GameContext context, ICollection<GameAction> actions)
        {
            var friendlyMinions = context.Board.FriendlyMinions;
            if(friendlyMinions.Count == 0)
                return;

            var opponentMinions = context.Board.OpponentMinions;
            var opponentHero = context.Board.OpponentHero;

            foreach(var minion in friendlyMinions)
            {
                var attack = minion.Attack ?? 0;
                if(attack <= 0)
                    continue;

                // Filter by live attack eligibility from HDT
                bool canAttackMinions = true, canAttackFace = true;
                try
                {
                    if(minion.EntityId.HasValue)
                    {
                        canAttackMinions = HdtEntityCombat.CanAttackMinionsNow(minion.EntityId.Value);
                        canAttackFace = HdtEntityCombat.CanAttackFaceNow(minion.EntityId.Value);
                    }
                }
                catch { }

                var minionName = string.IsNullOrWhiteSpace(minion.CardName)
                    ? minion.CardId ?? $"Minion#{minion.EntityId?.ToString() ?? "?"}"
                    : minion.CardName!;

                if(canAttackMinions)
                {
                    foreach(var target in opponentMinions)
                    {
                        if(target.IsStealthed)
                            continue;

                        var targetName = string.IsNullOrWhiteSpace(target.CardName)
                            ? target.CardId ?? $"Minion#{target.EntityId?.ToString() ?? "?"}"
                            : target.CardName!;

                        var priority = 700 + (attack * 10) - (target.Health ?? 0);
                        if(target.HasTaunt)
                            priority += 25;

                        var actionTarget = new ActionTarget(ParticipantSide.Opponent, target.EntityId, false, target.CardId, targetName);
                        var description = $"Attack with {minionName} -> {targetName}";
                        actions.Add(new GameAction(
                            GameActionType.Attack,
                            description,
                            priority,
                            card: null,
                            attacker: minion,
                            target: actionTarget,
                            requiresTarget: true));
                    }
                }

                if(opponentHero != null && canAttackFace)
                {
                    var priority = 650 + (attack * 10);
                    var actionTarget = new ActionTarget(ParticipantSide.Opponent, null, true, null, "Hero");
                    var description = $"Attack with {minionName} -> Opponent Hero";
                    actions.Add(new GameAction(
                        GameActionType.Attack,
                        description,
                        priority,
                        card: null,
                        attacker: minion,
                        target: actionTarget,
                        requiresTarget: true));
                }
            }
        }

        private static void GenerateHeroPower(GameContext context, ICollection<GameAction> actions)
        {
            var available = context.FriendlyMana.Available ?? context.FriendlyMana.Total ?? 0;
            if(available < 2)
                return;

            var description = "Use Hero Power";
            var action = new GameAction(
                GameActionType.HeroPower,
                description,
                priority: 400,
                card: null,
                attacker: null,
                target: null,
                requiresTarget: false);

            actions.Add(action);
        }

        private static void GenerateHeroAttacks(GameContext context, ICollection<GameAction> actions)
        {
            var hero = context.Board.FriendlyHero;
            if(hero.IsFrozen)
                return;

            var weapon = hero.Weapon;
            if(weapon == null)
                return;

            var attack = weapon.Attack ?? 0;
            var durability = weapon.Durability ?? 0;
            if(attack <= 0 || durability <= 0)
                return;

            var opponentMinions = context.Board.OpponentMinions;
            foreach(var target in opponentMinions)
            {
                if(target.IsStealthed)
                    continue;

                var targetName = string.IsNullOrWhiteSpace(target.CardName)
                    ? target.CardId ?? $"Minion#{target.EntityId?.ToString() ?? "?"}"
                    : target.CardName!;

                var priority = 600 + (attack * 10) - (target.Health ?? 0);
                if(target.HasTaunt)
                    priority += 25;

                var actionTarget = new ActionTarget(ParticipantSide.Opponent, target.EntityId, false, target.CardId, targetName);
                var description = $"Hero attack -> {targetName}";
                actions.Add(new GameAction(
                    GameActionType.HeroAttack,
                    description,
                    priority,
                    card: null,
                    attacker: null,
                    target: actionTarget,
                    requiresTarget: true,
                    heroAttacker: hero));
            }

            var opponentHero = context.Board.OpponentHero;
            if(opponentHero != null)
            {
                var priority = 550 + (attack * 10);
                var actionTarget = new ActionTarget(ParticipantSide.Opponent, null, true, null, "Hero");
                actions.Add(new GameAction(
                    GameActionType.HeroAttack,
                    "Hero attack -> Opponent Hero",
                    priority,
                    card: null,
                    attacker: null,
                    target: actionTarget,
                    requiresTarget: true,
                    heroAttacker: hero));
            }
        }

        private static GameAction CreateEndTurnAction()
        {
            return new GameAction(
                GameActionType.EndTurn,
                "End Turn",
                priority: 0,
                card: null,
                attacker: null,
                target: null,
                requiresTarget: false);
        }
    }
}
