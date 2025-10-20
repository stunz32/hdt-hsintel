using System;
using System.Collections.Generic;
using HSIntel.Core.Interfaces;
using HSIntel.Core.Models;
using HSIntel.Core.Models.Diagnostics;
using HSIntel.Core.Models.Events;

namespace HSIntel.Core.Services
{
    public sealed class StateValidator : IStateValidator
    {
        private const int MaxHandSize = 10;
        private const int MaxBoardSize = 7;
        private const int MaxHeroHealth = 30;

        public StateValidationResult Validate(GameContext context, DateTimeOffset timestamp)
        {
            var issues = new List<StateValidationIssue>();

            ValidateHero(context.Board.FriendlyHero, issues);
            ValidateHero(context.Board.OpponentHero, issues);

            ValidateMinions(context.Board.FriendlyMinions, ParticipantSide.Friendly, issues);
            ValidateMinions(context.Board.OpponentMinions, ParticipantSide.Opponent, issues);

            ValidateHand(context.FriendlyHand, true, issues);
            ValidateHand(context.OpponentHand, false, issues);

            ValidateSecrets(context.Board.Secrets, issues);

            if(issues.Count == 0)
                return StateValidationResult.Success(timestamp);

            return StateValidationResult.Failure(issues, timestamp);
        }

        private static void ValidateHero(HeroState hero, List<StateValidationIssue> issues)
        {
            if(hero.Health.HasValue && hero.Health.Value < 0)
            {
                issues.Add(new StateValidationIssue(
                    "hero.health.negative",
                    $"{hero.Side} hero has negative health ({hero.Health}).",
                    ValidationSeverity.Error));
            }

            if(hero.Health.HasValue && hero.Health.Value > MaxHeroHealth && hero.Side == ParticipantSide.Friendly)
            {
                issues.Add(new StateValidationIssue(
                    "hero.health.exceeds-max",
                    $"{hero.Side} hero health {hero.Health} exceeds normal cap {MaxHeroHealth}.",
                    ValidationSeverity.Warning));
            }

            if(hero.Armor.HasValue && hero.Armor.Value < 0)
            {
                issues.Add(new StateValidationIssue(
                    "hero.armor.negative",
                    $"{hero.Side} hero has negative armor ({hero.Armor}).",
                    ValidationSeverity.Error));
            }
        }

        private static void ValidateMinions(
            IReadOnlyList<MinionState> minions,
            ParticipantSide controller,
            List<StateValidationIssue> issues)
        {
            if(minions.Count > MaxBoardSize)
            {
                issues.Add(new StateValidationIssue(
                    $"board.{controller.ToString().ToLowerInvariant()}.too-many-minions",
                    $"{controller} board reports {minions.Count} minions (cap {MaxBoardSize}).",
                    ValidationSeverity.Error));
            }

            for(var i = 0; i < minions.Count; i++)
            {
                var minion = minions[i];

                if(minion.Position != i)
                {
                    issues.Add(new StateValidationIssue(
                        $"board.{controller.ToString().ToLowerInvariant()}.position-mismatch",
                        $"{controller} minion {minion.CardId ?? minion.EntityId?.ToString() ?? "unknown"} position {minion.Position} expected {i}.",
                        ValidationSeverity.Warning));
                }

                if(minion.Health.HasValue && minion.Health.Value < 0)
                {
                    issues.Add(new StateValidationIssue(
                        $"board.{controller.ToString().ToLowerInvariant()}.negative-health",
                        $"{controller} minion {minion.CardId ?? minion.EntityId?.ToString() ?? "unknown"} has negative health ({minion.Health}).",
                        ValidationSeverity.Error));
                }

                if(minion.Attack.HasValue && minion.Attack.Value < 0)
                {
                    issues.Add(new StateValidationIssue(
                        $"board.{controller.ToString().ToLowerInvariant()}.negative-attack",
                        $"{controller} minion {minion.CardId ?? minion.EntityId?.ToString() ?? "unknown"} has negative attack ({minion.Attack}).",
                        ValidationSeverity.Error));
                }
            }
        }

        private static void ValidateHand(HandState hand, bool isFriendly, List<StateValidationIssue> issues)
        {
            if(hand.TotalCardCount.HasValue && hand.TotalCardCount.Value > MaxHandSize)
            {
                issues.Add(new StateValidationIssue(
                    $"hand.{hand.Owner.ToString().ToLowerInvariant()}.too-many-cards",
                    $"{hand.Owner} hand reports {hand.TotalCardCount} cards (cap {MaxHandSize}).",
                    ValidationSeverity.Error));
            }

            var expectedCount = hand.TotalCardCount ?? hand.Cards.Count;

            if(isFriendly && expectedCount != hand.Cards.Count)
            {
                issues.Add(new StateValidationIssue(
                    "hand.friendly.count-mismatch",
                    $"Friendly hand total {expectedCount} does not match detailed entries {hand.Cards.Count}.",
                    ValidationSeverity.Warning));
            }

            foreach(var card in hand.Cards)
            {
                if(card.BaseCost.HasValue && card.BaseCost.Value < 0)
                {
                    issues.Add(new StateValidationIssue(
                        $"hand.{hand.Owner.ToString().ToLowerInvariant()}.negative-base-cost",
                        $"{hand.Owner} card {card.CardId ?? card.EntityId?.ToString() ?? "unknown"} has negative base cost ({card.BaseCost}).",
                        ValidationSeverity.Error));
                }

                if(card.EffectiveCost.HasValue && card.EffectiveCost.Value < 0)
                {
                    issues.Add(new StateValidationIssue(
                        $"hand.{hand.Owner.ToString().ToLowerInvariant()}.negative-effective-cost",
                        $"{hand.Owner} card {card.CardId ?? card.EntityId?.ToString() ?? "unknown"} has negative effective cost ({card.EffectiveCost}).",
                        ValidationSeverity.Error));
                }
            }
        }

        private static void ValidateSecrets(IReadOnlyList<SecretState> secrets, List<StateValidationIssue> issues)
        {
            foreach(var secret in secrets)
            {
                if(secret.TurnsInPlay < 0)
                {
                    issues.Add(new StateValidationIssue(
                        "secret.negative-turns",
                        $"Secret {secret.CardId ?? secret.EntityId?.ToString() ?? "unknown"} reports negative turns in play ({secret.TurnsInPlay}).",
                        ValidationSeverity.Warning));
                }
            }
        }
    }
}
