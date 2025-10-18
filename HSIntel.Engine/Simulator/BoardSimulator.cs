using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HSIntel.Core.Models;
using HSIntel.Core.Models.Events;
using HSIntel.Engine.Models;

namespace HSIntel.Engine.Simulator
{
    /// <summary>
    /// Applies generated actions to the current immutable game context to produce forecast states.
    /// </summary>
    public sealed class BoardSimulator
    {
        public SimulationResult Simulate(GameContext context, GameAction action)
        {
            if(context == null)
                throw new ArgumentNullException(nameof(context));
            if(action == null)
                throw new ArgumentNullException(nameof(action));

            return action.ActionType switch
            {
                GameActionType.PlayCard => SimulatePlayCard(context, action),
                GameActionType.Attack => SimulateAttack(context, action),
                GameActionType.HeroPower => SimulateHeroPower(context),
                GameActionType.EndTurn => SimulateEndTurn(context),
                _ => new SimulationResult(context, SimulationMetrics.Empty, false, $"Unsupported action type {action.ActionType}")
            };
        }

        private static SimulationResult SimulatePlayCard(GameContext context, GameAction action)
        {
            if(action.Card == null)
                return new SimulationResult(context, SimulationMetrics.Empty, false, "PlayCard missing card context");

            var card = action.Card;
            var handCards = new List<HandCardState>(context.FriendlyHand.Cards);
            var removed = RemoveHandCard(handCards, card);

            if(!removed)
                return new SimulationResult(context, SimulationMetrics.Empty, false, "Card not found in hand snapshot");

            var updatedHand = context.FriendlyHand.With(cards: handCards, totalCardCount: Math.Max(0, (context.FriendlyHand.TotalCardCount ?? handCards.Count) - 1));

            var cost = card.EffectiveCost ?? card.BaseCost ?? 0;
            var manaAvailable = context.FriendlyMana.Available ?? context.FriendlyMana.Total ?? 0;
            var manaRemaining = Math.Max(0, manaAvailable - Math.Max(0, cost));
            var updatedMana = context.FriendlyMana.With(available: manaRemaining);

            var updatedContext = context.With(
                friendlyHand: updatedHand,
                friendlyMana: updatedMana,
                lastUpdated: DateTimeOffset.UtcNow);

            var metrics = new SimulationMetrics(
                friendlyCardsPlayed: 1,
                friendlyAttacks: 0,
                friendlyManaSpent: Math.Max(0, cost),
                friendlyHeroPowerUses: 0,
                friendlyMinionsRemoved: 0,
                opponentMinionsRemoved: 0,
                damageDealtToOpponentHero: 0,
                damageTakenByFriendlyHero: 0);

            Trace.WriteLine($"[HSIntel][Engine] SimulatePlayCard => mana {manaAvailable}->{manaRemaining}");
            return new SimulationResult(updatedContext, metrics, true);
        }

        private static SimulationResult SimulateAttack(GameContext context, GameAction action)
        {
            if(action.Attacker == null)
                return new SimulationResult(context, SimulationMetrics.Empty, false, "Attack missing attacker");
            if(action.Target == null)
                return new SimulationResult(context, SimulationMetrics.Empty, false, "Attack missing target");

            var attacker = action.Attacker;
            var target = action.Target;

            var friendlyMinions = CloneMinions(context.Board.FriendlyMinions);
            var opponentMinions = CloneMinions(context.Board.OpponentMinions);

            var attackerIndex = FindMinionIndex(friendlyMinions, attacker);
            if(attackerIndex < 0)
                return new SimulationResult(context, SimulationMetrics.Empty, false, "Attacking minion not present on board");

            var attackerClone = friendlyMinions[attackerIndex];
            var attackerAttack = attackerClone.Attack ?? 0;
            var attackerHealthBefore = attackerClone.Health ?? 0;

            HeroState friendlyHero = context.Board.FriendlyHero;
            HeroState opponentHero = context.Board.OpponentHero;

            var friendlyMinionsRemoved = 0;
            var opponentMinionsRemoved = 0;
            var damageToOpponentHero = 0;
            var damageToFriendlyHero = 0;

            // Capture target pre-state for concise summary logging
            int targetHealthBefore = 0;
            string targetNameForLog = target.IsHero ? "Opponent Hero" : "Opponent Minion";
            if(!target.IsHero)
            {
                var idx = FindMinionIndex(opponentMinions, target);
                if(idx >= 0)
                {
                    var t = opponentMinions[idx];
                    targetHealthBefore = t.Health ?? 0;
                    targetNameForLog = string.IsNullOrWhiteSpace(t.CardName) ? (t.CardId ?? "Minion") : t.CardName!;
                }
            }

            if(target.IsHero)
            {
                var heroHealth = opponentHero.Health ?? 0;
                var heroArmor = opponentHero.Armor ?? 0;
                var effectiveHealth = heroHealth + heroArmor;
                var damage = Math.Max(0, attackerAttack);
                var armorAbsorbed = Math.Min(heroArmor, damage);
                var healthDamage = damage - armorAbsorbed;
                var newArmor = Math.Max(0, heroArmor - armorAbsorbed);
                var newHealth = Math.Max(0, heroHealth - healthDamage);
                damageToOpponentHero = Math.Max(0, Math.Min(damage, effectiveHealth));

                opponentHero = new HeroState(
                    opponentHero.Side,
                    newHealth,
                    newArmor,
                    opponentHero.Weapon,
                    opponentHero.IsImmune,
                    opponentHero.IsFrozen);
            }
            else
            {
                var targetIndex = FindMinionIndex(opponentMinions, target);
                if(targetIndex < 0)
                    return new SimulationResult(context, SimulationMetrics.Empty, false, "Target minion not present on board");

                var targetMinion = opponentMinions[targetIndex];
                var targetAttack = targetMinion.Attack ?? 0;
                var targetHealth = targetMinion.Health ?? 0;
                var targetDivineShield = targetMinion.HasDivineShield;

                if(targetDivineShield)
                {
                    opponentMinions[targetIndex] = CopyMinion(targetMinion, hasDivineShield: false);
                }
                else
                {
                    var remaining = targetHealth - Math.Max(0, attackerAttack);
                    if(remaining <= 0)
                    {
                        opponentMinions.RemoveAt(targetIndex);
                        opponentMinionsRemoved++;
                    }
                    else
                    {
                        opponentMinions[targetIndex] = CopyMinion(targetMinion, health: remaining);
                    }
                }

                var attackerDivineShield = attackerClone.HasDivineShield;
                if(attackerDivineShield && targetAttack > 0)
                {
                    friendlyMinions[attackerIndex] = CopyMinion(attackerClone, hasDivineShield: false);
                }
                else if(targetAttack > 0)
                {
                    var attackerHealth = attackerClone.Health ?? 0;
                    var attackerRemaining = attackerHealth - targetAttack;
                    if(attackerRemaining <= 0)
                    {
                        friendlyMinions.RemoveAt(attackerIndex);
                        friendlyMinionsRemoved++;
                    }
                    else
                    {
                        friendlyMinions[attackerIndex] = CopyMinion(attackerClone, health: attackerRemaining);
                    }
                }
            }

            var updatedBoard = context.Board.With(
                friendlyMinions: ReindexMinions(friendlyMinions),
                opponentMinions: ReindexMinions(opponentMinions),
                friendlyHero: friendlyHero,
                opponentHero: opponentHero,
                lastUpdated: DateTimeOffset.UtcNow);

            var updatedContext = context.With(board: updatedBoard, lastUpdated: DateTimeOffset.UtcNow);

            var metrics = new SimulationMetrics(
                friendlyCardsPlayed: 0,
                friendlyAttacks: 1,
                friendlyManaSpent: 0,
                friendlyHeroPowerUses: 0,
                friendlyMinionsRemoved: friendlyMinionsRemoved,
                opponentMinionsRemoved: opponentMinionsRemoved,
                damageDealtToOpponentHero: damageToOpponentHero,
                damageTakenByFriendlyHero: damageToFriendlyHero);

            // Build concise attack summary log (attacker/target health before -> after)
            try
            {
                var attackerIdxPost = FindMinionIndex(updatedBoard.FriendlyMinions.ToList(), attackerClone);
                var attackerHealthAfter = attackerIdxPost >= 0 ? (updatedBoard.FriendlyMinions[attackerIdxPost].Health ?? 0) : 0;

                int targetHealthAfter;
                if(target.IsHero)
                {
                    var oppHeroPost = updatedBoard.OpponentHero;
                    targetHealthAfter = (oppHeroPost.Health ?? 0) + (oppHeroPost.Armor ?? 0);
                    targetNameForLog = "Opponent Hero";
                }
                else
                {
                    var tIdxPost = FindMinionIndex(updatedBoard.OpponentMinions.ToList(), target);
                    targetHealthAfter = tIdxPost >= 0 ? (updatedBoard.OpponentMinions[tIdxPost].Health ?? 0) : 0;
                }

                var attackerNameForLog = string.IsNullOrWhiteSpace(attackerClone.CardName)
                    ? (attackerClone.CardId ?? $"Minion#{attackerClone.EntityId?.ToString() ?? "?"}")
                    : attackerClone.CardName!;

                Trace.WriteLine($"[HSIntel][Engine] AttackSummary: {attackerNameForLog} {attackerHealthBefore}->{attackerHealthAfter} vs {targetNameForLog} {targetHealthBefore}->{targetHealthAfter} removed F={friendlyMinionsRemoved} O={opponentMinionsRemoved}");
            }
            catch { }

            return new SimulationResult(updatedContext, metrics, true);
        }

        private static SimulationResult SimulateHeroPower(GameContext context)
        {
            var available = context.FriendlyMana.Available ?? context.FriendlyMana.Total ?? 0;
            if(available < 2)
                available = context.FriendlyMana.Total ?? available;

            var manaRemaining = Math.Max(0, available - 2);
            var updatedMana = context.FriendlyMana.With(available: manaRemaining);

            var updatedContext = context.With(
                friendlyMana: updatedMana,
                lastUpdated: DateTimeOffset.UtcNow);

            var metrics = new SimulationMetrics(
                friendlyCardsPlayed: 0,
                friendlyAttacks: 0,
                friendlyManaSpent: Math.Min(2, available),
                friendlyHeroPowerUses: 1,
                friendlyMinionsRemoved: 0,
                opponentMinionsRemoved: 0,
                damageDealtToOpponentHero: 0,
                damageTakenByFriendlyHero: 0);

            Trace.WriteLine($"[HSIntel][Engine] SimulateHeroPower => mana {available}->{manaRemaining}");
            return new SimulationResult(updatedContext, metrics, true);
        }

        private static SimulationResult SimulateEndTurn(GameContext context)
        {
            var nextTurn = (context.TurnNumber ?? 0) + 1;
            var mana = context.FriendlyMana.With(available: context.FriendlyMana.Total ?? context.FriendlyMana.Available);

            var updatedContext = context.With(
                activeSide: ParticipantSide.Opponent,
                turnNumber: nextTurn,
                friendlyMana: mana,
                lastUpdated: DateTimeOffset.UtcNow);

            return new SimulationResult(updatedContext, SimulationMetrics.Empty, true);
        }

        private static bool RemoveHandCard(List<HandCardState> cards, HandCardState candidate)
        {
            for(var i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                if(card.EntityId.HasValue && candidate.EntityId.HasValue && card.EntityId == candidate.EntityId)
                {
                    cards.RemoveAt(i);
                    return true;
                }

                if(!string.IsNullOrEmpty(candidate.CardId) && string.Equals(card.CardId, candidate.CardId, StringComparison.OrdinalIgnoreCase))
                {
                    cards.RemoveAt(i);
                    return true;
                }
            }

            if(cards.Count > 0)
            {
                cards.RemoveAt(0);
                return true;
            }

            return false;
        }

        private static List<MinionState> CloneMinions(IReadOnlyList<MinionState> source)
        {
            if(source.Count == 0)
                return new List<MinionState>();

            var list = new List<MinionState>(source.Count);
            foreach(var minion in source)
                list.Add(CopyMinion(minion));
            return list;
        }

        private static MinionState CopyMinion(MinionState minion, int? health = null, bool? hasDivineShield = null)
        {
            return new MinionState(
                minion.Controller,
                minion.EntityId,
                minion.CardId,
                minion.CardName,
                minion.Attack,
                health ?? minion.Health,
                minion.Position,
                minion.Keywords,
                hasDivineShield ?? minion.HasDivineShield,
                minion.HasTaunt,
                minion.IsStealthed,
                minion.IsFrozen,
                minion.IsDormant);
        }

        private static int FindMinionIndex(List<MinionState> minions, MinionState reference)
        {
            for(var i = 0; i < minions.Count; i++)
            {
                var candidate = minions[i];
                if(reference.EntityId.HasValue && candidate.EntityId.HasValue && reference.EntityId == candidate.EntityId)
                    return i;
                if(!string.IsNullOrEmpty(reference.CardId) && string.Equals(reference.CardId, candidate.CardId, StringComparison.OrdinalIgnoreCase) && reference.Position == candidate.Position)
                    return i;
            }

            return -1;
        }

        private static int FindMinionIndex(List<MinionState> minions, ActionTarget target)
        {
            for(var i = 0; i < minions.Count; i++)
            {
                var candidate = minions[i];
                if(target.EntityId.HasValue && candidate.EntityId.HasValue && target.EntityId == candidate.EntityId)
                    return i;
                if(!string.IsNullOrEmpty(target.CardId) && string.Equals(target.CardId, candidate.CardId, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }

        private static IReadOnlyList<MinionState> ReindexMinions(List<MinionState> minions)
        {
            if(minions.Count == 0)
                return Array.Empty<MinionState>();

            for(var i = 0; i < minions.Count; i++)
            {
                var minion = minions[i];
                if(minion.Position != i)
                {
                    minions[i] = new MinionState(
                        minion.Controller,
                        minion.EntityId,
                        minion.CardId,
                        minion.CardName,
                        minion.Attack,
                        minion.Health,
                        i,
                        minion.Keywords,
                        minion.HasDivineShield,
                        minion.HasTaunt,
                        minion.IsStealthed,
                        minion.IsFrozen,
                        minion.IsDormant);
                }
            }

            return minions;
        }
    }
}
