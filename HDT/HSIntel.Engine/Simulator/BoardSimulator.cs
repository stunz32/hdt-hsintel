using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HSIntel.Core.Models;
using HSIntel.Core.Models.Events;
using HSIntel.Engine.Models;
using HSIntel.Engine.Data;
using HSIntel.Engine.Internal;

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
                GameActionType.HeroAttack => SimulateHeroAttack(context, action),
                GameActionType.HeroPower => SimulateHeroPower(context, action),
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

            // Curated direct damage spells (internal toggle)
            try
            {
                if(EngineToggles.EnableCuratedSpells && DirectDamageProvider.TryGetByIdOrName(card.CardId, card.CardName, out var info))
                {
                    var board = updatedContext.Board;
                    var oppHero = board.OpponentHero;
                    var oppMinions = CloneMinions(board.OpponentMinions);
                    var friendlyMinions = CloneMinions(board.FriendlyMinions);
                    var friendlyHero = board.FriendlyHero;

                    int oppRemoved = 0;
                    int dmgToHero = 0;

                    int dmg = Math.Max(0, info.Damage);
                    if(dmg > 0)
                    {
                        if(info.Target == DamageTargetKind.Aoe)
                        {
                            if(oppHero != null)
                            {
                                var h = oppHero.Health ?? 0;
                                var a = oppHero.Armor ?? 0;
                                var absorb = Math.Min(a, dmg);
                                var healthDmg = dmg - absorb;
                                dmgToHero = Math.Max(0, Math.Min(dmg, h + a));
                                oppHero = oppHero.With(health: Math.Max(0, h - healthDmg), armor: Math.Max(0, a - absorb));
                            }
                        }
                        else if(info.Target == DamageTargetKind.Face || info.Target == DamageTargetKind.Flex || (info.Target == DamageTargetKind.Random && action.Target != null && action.Target.IsHero))
                        {
                            if(oppHero != null)
                            {
                                var h = oppHero.Health ?? 0;
                                var a = oppHero.Armor ?? 0;
                                var absorb = Math.Min(a, dmg);
                                var healthDmg = dmg - absorb;
                                dmgToHero = Math.Max(0, Math.Min(dmg, h + a));
                                oppHero = oppHero.With(health: Math.Max(0, h - healthDmg), armor: Math.Max(0, a - absorb));
                            }
                        }
                        else if(info.Target == DamageTargetKind.Minion || (info.Target == DamageTargetKind.Flex && action.Target != null && !action.Target.IsHero))
                        {
                            if(action.Target != null)
                            {
                                var idx = FindMinionIndex(oppMinions, action.Target);
                                if(idx >= 0)
                                {
                                    var t = oppMinions[idx];
                                    var remain = (t.Health ?? 0) - dmg;
                                    if(remain <= 0)
                                    {
                                        oppMinions.RemoveAt(idx);
                                        oppRemoved++;
                                    }
                                    else
                                    {
                                        oppMinions[idx] = CopyMinion(t, health: remain);
                                    }
                                }
                            }
                        }

                        var newBoard = board.With(
                            friendlyMinions: ReindexMinions(friendlyMinions),
                            opponentMinions: ReindexMinions(oppMinions),
                            friendlyHero: friendlyHero,
                            opponentHero: oppHero,
                            lastUpdated: DateTimeOffset.UtcNow);
                        updatedContext = updatedContext.With(board: newBoard, lastUpdated: DateTimeOffset.UtcNow);

                        metrics = new SimulationMetrics(
                            friendlyCardsPlayed: 1,
                            friendlyAttacks: 0,
                            friendlyManaSpent: Math.Max(0, cost),
                            friendlyHeroPowerUses: 0,
                            friendlyMinionsRemoved: 0,
                            opponentMinionsRemoved: oppRemoved,
                            damageDealtToOpponentHero: dmgToHero,
                            damageTakenByFriendlyHero: 0);
                    }
                }
            }
            catch { }

            // Heuristic: if we do not recognize the card as a curated direct-damage spell,
            // assume many plays increase friendly board presence this turn (e.g., minion/summon).
            // We add a non-attacking body so later steps do not try to attack with it.
            try
            {
                if(EngineToggles.EnableHeuristicMinionSummon)
                {
                    bool isCuratedDamage = false;
                    try { isCuratedDamage = DirectDamageProvider.TryGet(card.CardId, out var _); } catch { }
                    if(!isCuratedDamage)
                    {
                        var board = updatedContext.Board;
                        var friendlyMinions = CloneMinions(board.FriendlyMinions);

                        var est = EstimateBodyFromCost(card.EffectiveCost ?? card.BaseCost ?? 0);
                        if(est.health > 0)
                        {
                            var phantom = new MinionState(
                                HSIntel.Core.Models.Events.ParticipantSide.Friendly,
                                entityId: null,
                                cardId: card.CardId,
                                cardName: string.IsNullOrWhiteSpace(card.CardName) ? card.CardId : $"{card.CardName}",
                                attack: est.attack,
                                health: est.health,
                                position: Math.Max(0, friendlyMinions.Count),
                                keywords: Array.Empty<string>(),
                                hasDivineShield: false,
                                hasTaunt: false,
                                isStealthed: false,
                                isFrozen: false,
                                isDormant: true // cannot attack this turn
                            );
                            friendlyMinions.Add(phantom);
                            var newBoard = board.With(friendlyMinions: ReindexMinions(friendlyMinions));
                            updatedContext = updatedContext.With(board: newBoard, lastUpdated: DateTimeOffset.UtcNow);
                        }
                    }
                }
            }
            catch { }

            Trace.WriteLine($"[HSIntel][Engine] SimulatePlayCard => mana {manaAvailable}->{manaRemaining}");
            return new SimulationResult(updatedContext, metrics, true);
        }

        private static (int attack, int health) EstimateBodyFromCost(int cost)
        {
            // Conservative stat estimate to avoid overvaluing: skew toward health, low attack, no charge.
            if(cost <= 0) return (0, 0);
            var attack = Math.Max(0, cost - 2);         // 1->0, 2->0, 3->1, 4->2, ...
            var health = Math.Max(1, cost + 1);         // 1->2, 2->3, 3->4, ...
            return (attack, health);
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

                opponentHero = opponentHero.With(
                    health: newHealth,
                    armor: newArmor);
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

        private static SimulationResult SimulateHeroAttack(GameContext context, GameAction action)
        {
            if(action.Target == null)
                return new SimulationResult(context, SimulationMetrics.Empty, false, "HeroAttack missing target");

            var friendlyHero = context.Board.FriendlyHero;
            var weapon = friendlyHero.Weapon;

            if(weapon == null)
                return new SimulationResult(context, SimulationMetrics.Empty, false, "HeroAttack requires equipped weapon");

            var attack = weapon.Attack ?? 0;
            if(attack <= 0)
                return new SimulationResult(context, SimulationMetrics.Empty, false, "HeroAttack weapon has no attack");

            var durability = weapon.Durability ?? 0;
            if(durability <= 0)
                return new SimulationResult(context, SimulationMetrics.Empty, false, "HeroAttack weapon has no durability");

            var opponentHero = context.Board.OpponentHero;
            var opponentMinions = CloneMinions(context.Board.OpponentMinions);

            var damageToOpponentHero = 0;
            var damageToFriendlyHero = 0;
            var opponentMinionsRemoved = 0;

            // Pre-capture for AttackSummary
            int attackerHealthBefore = (friendlyHero.Health ?? 0) + (friendlyHero.Armor ?? 0);
            int targetHealthBefore = 0;
            string targetNameForLog = action.Target.IsHero ? "Opponent Hero" : "Opponent Minion";

            if(action.Target.IsHero)
            {
                var heroHealth = opponentHero.Health ?? 0;
                var heroArmor = opponentHero.Armor ?? 0;
                var effectiveHealth = heroHealth + heroArmor;
                var damage = Math.Max(0, attack);
                var armorAbsorbed = Math.Min(heroArmor, damage);
                var healthDamage = damage - armorAbsorbed;

                var newArmor = Math.Max(0, heroArmor - armorAbsorbed);
                var newHealth = Math.Max(0, heroHealth - healthDamage);
                damageToOpponentHero = Math.Max(0, Math.Min(damage, effectiveHealth));

                opponentHero = opponentHero.With(health: newHealth, armor: newArmor);
                targetHealthBefore = heroHealth + heroArmor;
            }
            else
            {
                var targetIndex = FindMinionIndex(opponentMinions, action.Target);
                if(targetIndex < 0)
                    return new SimulationResult(context, SimulationMetrics.Empty, false, "HeroAttack target not present on board");

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
                    var remaining = targetHealth - Math.Max(0, attack);
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

                if(targetAttack > 0 && !friendlyHero.IsImmune)
                {
                    var heroHealth = friendlyHero.Health ?? 0;
                    var heroArmor = friendlyHero.Armor ?? 0;
                    var armorAbsorbed = Math.Min(heroArmor, targetAttack);
                    var healthDamage = targetAttack - armorAbsorbed;
                    var newArmor = Math.Max(0, heroArmor - armorAbsorbed);
                    var newHealth = Math.Max(0, heroHealth - Math.Max(0, healthDamage));
                    damageToFriendlyHero = Math.Max(0, Math.Min(targetAttack, heroHealth + heroArmor));
                    friendlyHero = friendlyHero.With(health: newHealth, armor: newArmor);
                }
                targetHealthBefore = targetHealth;
            }

            WeaponState? updatedWeapon = null;
            if(durability - 1 > 0)
            {
                updatedWeapon = new WeaponState(
                    weapon.EntityId,
                    weapon.CardId,
                    weapon.CardName,
                    weapon.Attack,
                    durability - 1,
                    weapon.IsPoisonous,
                    weapon.HasWindfury);
            }

            friendlyHero = friendlyHero.With(weapon: updatedWeapon);

            var updatedBoard = context.Board.With(
                friendlyHero: friendlyHero,
                opponentHero: opponentHero,
                opponentMinions: opponentMinions);

            var updatedContext = context.With(
                board: updatedBoard,
                lastUpdated: DateTimeOffset.UtcNow);

            var metrics = new SimulationMetrics(
                friendlyCardsPlayed: 0,
                friendlyAttacks: 1,
                friendlyManaSpent: 0,
                friendlyHeroPowerUses: 0,
                friendlyMinionsRemoved: 0,
                opponentMinionsRemoved: opponentMinionsRemoved,
                damageDealtToOpponentHero: damageToOpponentHero,
                damageTakenByFriendlyHero: damageToFriendlyHero);

            // AttackSummary for hero swings with attacker label "Friendly Hero"
            try
            {
                int attackerHealthAfter = (updatedBoard.FriendlyHero.Health ?? 0) + (updatedBoard.FriendlyHero.Armor ?? 0);
                int targetHealthAfter;
                if(action.Target.IsHero)
                {
                    var opp = updatedBoard.OpponentHero;
                    targetHealthAfter = (opp.Health ?? 0) + (opp.Armor ?? 0);
                    targetNameForLog = "Opponent Hero";
                }
                else
                {
                    var tIdxPost = FindMinionIndex(updatedBoard.OpponentMinions.ToList(), action.Target);
                    targetHealthAfter = tIdxPost >= 0 ? (updatedBoard.OpponentMinions[tIdxPost].Health ?? 0) : 0;
                }
                Trace.WriteLine($"[HSIntel][Engine] AttackSummary: Friendly Hero {attackerHealthBefore}->{attackerHealthAfter} vs {targetNameForLog} {targetHealthBefore}->{targetHealthAfter} removed F=0 O={opponentMinionsRemoved}");
            }
            catch { }

            Trace.WriteLine($"[HSIntel][Engine] SimulateHeroAttack => target={(action.Target.IsHero ? "Hero" : "Minion")} damage={attack} heroDamageTaken={damageToFriendlyHero}");

            return new SimulationResult(updatedContext, metrics, true);
        }

        private static SimulationResult SimulateHeroPower(GameContext context, GameAction action)
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

            // Curated hero power damage (internal toggle)
            try
            {
                if(EngineToggles.EnableCuratedHeroPowerDamage)
                {
                    var hero = updatedContext.Board.FriendlyHero;
                    if(hero != null && (HeroPowerProvider.TryGetByCardId(hero.HeroPowerCardId, out var hp) || HeroPowerProvider.TryGetByClass(hero.HeroClass, out hp)))
                    {
                        int dmg = Math.Max(0, hp.Damage);
                        if(dmg > 0)
                        {
                            var board = updatedContext.Board;
                            var oppHero = board.OpponentHero;
                            var oppMinions = CloneMinions(board.OpponentMinions);
                            int dmgToHero = 0;

                            bool toFace = hp.Target == DamageTargetKind.Face || hp.Target == DamageTargetKind.Aoe || (hp.Target == DamageTargetKind.Flex && (action?.Target == null || action.Target.IsHero));
                            if(toFace)
                            {
                                if(oppHero != null)
                                {
                                    var h = oppHero.Health ?? 0;
                                    var a = oppHero.Armor ?? 0;
                                    var absorb = Math.Min(a, dmg);
                                    var healthDmg = dmg - absorb;
                                    dmgToHero = Math.Max(0, Math.Min(dmg, h + a));
                                    oppHero = oppHero.With(health: Math.Max(0, h - healthDmg), armor: Math.Max(0, a - absorb));
                                }
                            }
                            else if(hp.Target == DamageTargetKind.Minion || (hp.Target == DamageTargetKind.Flex && action?.Target != null && !action.Target.IsHero))
                            {
                                var idx = action?.Target != null ? FindMinionIndex(oppMinions, action.Target) : -1;
                                if(idx >= 0)
                                {
                                    var t = oppMinions[idx];
                                    var remain = (t.Health ?? 0) - dmg;
                                    if(remain <= 0)
                                        oppMinions.RemoveAt(idx);
                                    else
                                        oppMinions[idx] = CopyMinion(t, health: remain);
                                }
                            }

                            var newBoard = board.With(opponentHero: oppHero, opponentMinions: ReindexMinions(oppMinions));
                            updatedContext = updatedContext.With(board: newBoard, lastUpdated: DateTimeOffset.UtcNow);

                            metrics = new SimulationMetrics(
                                friendlyCardsPlayed: 0,
                                friendlyAttacks: 0,
                                friendlyManaSpent: Math.Min(2, available),
                                friendlyHeroPowerUses: 1,
                                friendlyMinionsRemoved: 0,
                                opponentMinionsRemoved: 0,
                                damageDealtToOpponentHero: dmgToHero,
                                damageTakenByFriendlyHero: 0);
                        }
                    }
                }
            }
            catch { }

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
