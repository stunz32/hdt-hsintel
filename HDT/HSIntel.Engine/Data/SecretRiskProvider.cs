using System;
using HSIntel.Core.Models;
using HSIntel.Engine.Models;
using HSIntel.Core.Models.Events;

namespace HSIntel.Engine.Data
{
    /// <summary>
    /// Very lightweight, internal risk signal for ordering only.
    /// No public API changes; pure heuristic penalty guidance.
    /// </summary>
    internal static class SecretRiskProvider
    {
        // Returns a small negative penalty to de-prioritize risky lines when opponent secrets are present.
        public static int GetOrderingPenalty(GameContext context, GameAction action)
        {
            if(context?.Board?.Secrets == null || action == null)
                return 0;

            var oppSecrets = 0;
            foreach(var s in context.Board.Secrets)
                if(s.Controller == ParticipantSide.Opponent)
                    oppSecrets++;

            if(oppSecrets == 0)
                return 0;

            // Heuristic: first expensive spell play gets a moderate penalty (possible Counterspell)
            if(action.ActionType == GameActionType.PlayCard && action.Card != null)
            {
                var cost = action.Card.EffectiveCost ?? action.Card.BaseCost ?? 0;
                if(cost >= 4)
                    return -15;
            }

            // Face attacks slightly de-prioritized behind probes
            if(action.ActionType == GameActionType.Attack && action.Target != null && action.Target.IsHero)
                return -5;

            return 0;
        }
    }
}
