using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HSIntel.Core.Models;
using HSIntel.Core.Models.Events;
using HSIntel.Engine.Models;

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
            GenerateHeroPower(context, actions);

            actions.Add(CreateEndTurnAction());

            var ordered = actions
                .OrderByDescending(a => a.Priority)
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

                // Respect live readiness using HDT data when available
                var eid = minion.EntityId;
                bool canAttackMinions = true, canAttackFace = true;
                try
                {
                    if(eid.HasValue)
                    {
                        canAttackMinions = HSIntel.Engine.Internal.HdtEntityCombat.CanAttackMinionsNow(eid.Value);
                        canAttackFace = HSIntel.Engine.Internal.HdtEntityCombat.CanAttackFaceNow(eid.Value);
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
