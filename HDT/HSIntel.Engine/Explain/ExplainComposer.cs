using System;
using System.Collections.Generic;
using System.Linq;
using HSIntel.Engine.Models;
using HSIntel.Engine.Data;

namespace HSIntel.Engine.Explain
{
    // Internal-only explanation composer. Does not change public API or logs.
    internal static class ExplainComposer
    {
        internal static ExplanationSet Compose(SearchResult result)
        {
            var entries = new List<ExplanationEntry>(result.Actions.Count);

            // Rough overall confidence from score; clamp to [0,1].
            var score = result.Evaluation.TotalScore;
            var overallConf = NormalizeScore(score);

            var isLethal = IsLethal(result);

            for(int i = 0; i < result.Actions.Count; i++)
            {
                var a = result.Actions[i];
                var tokens = InferTokens(a, isLethal);
                var medium = ComposeMedium(a, tokens);
                var full = ComposeFull(a, tokens);
                var conf = AdjustConfidence(overallConf, i, tokens);
                entries.Add(new ExplanationEntry(i + 1, tokens, medium, full, conf));
            }

            var rngProb = EstimateRngProbability(result);
            return new ExplanationSet(entries, isLethal, overallConf, rngProb, DateTimeOffset.UtcNow);
        }

        private static string ComposeMedium(GameAction a, IReadOnlyList<string> tokens)
        {
            // One sentence: explicit instruction + short reason
            var action = DescribeAction(a);
            var reason = MediumFor(tokens);
            if(string.IsNullOrWhiteSpace(action))
                return reason;
            if(string.IsNullOrWhiteSpace(reason))
                return action;
            return action + ". " + reason;
        }

        private static string ComposeFull(GameAction a, IReadOnlyList<string> tokens)
        {
            var action = DescribeAction(a);
            var detail = FullFor(tokens);
            if(string.IsNullOrWhiteSpace(action))
                return detail;
            if(string.IsNullOrWhiteSpace(detail))
                return "Action: " + action + ".";
            return "Action: " + action + ". " + detail;
        }

        private static string DescribeAction(GameAction a)
        {
            try
            {
                switch(a.ActionType)
                {
                    case GameActionType.PlayCard:
                    {
                        var card = a.Card;
                        var name = SafeName(card?.CardName, card?.CardId, fallback: "card");
                        if(a.RequiresTarget && a.Target != null)
                            return $"Play {name} targeting {DescribeTarget(a.Target)}";
                        return $"Play {name}";
                    }
                    case GameActionType.Attack:
                    {
                        var atk = a.Attacker;
                        var attackerName = SafeName(atk?.CardName, atk?.CardId, fallback: "minion");
                        var target = a.Target != null ? DescribeTarget(a.Target) : "target";
                        return $"Attack {target} with {attackerName}";
                    }
                    case GameActionType.HeroAttack:
                    {
                        var target = a.Target != null ? DescribeTarget(a.Target) : "target";
                        return $"Attack {target} with your hero";
                    }
                    case GameActionType.HeroPower:
                    {
                        var hpName = a.HeroAttacker?.HeroPowerCardName;
                        var label = string.IsNullOrWhiteSpace(hpName) ? "Hero Power" : hpName;
                        if(a.RequiresTarget && a.Target != null)
                            return $"Use {label} on {DescribeTarget(a.Target)}";
                        return $"Use {label}";
                    }
                    case GameActionType.EndTurn:
                        return "End the turn";
                }
            }
            catch { }
            return string.Empty;
        }

        private static string DescribeTarget(ActionTarget t)
        {
            var side = t.Side == HSIntel.Core.Models.Events.ParticipantSide.Friendly ? "your" : "opponent";
            if(t.IsHero)
                return side + " hero";
            var name = SafeName(t.CardName, t.CardId, fallback: "minion");
            return side + " " + name;
        }

        private static double AdjustConfidence(double overall, int index, IReadOnlyList<string> tokens)
        {
            // Earlier steps higher confidence; removal/lethal add a small boost.
            double conf = overall * (1.0 - Math.Min(0.35, index * 0.08));
            if(tokens.Contains("lethal.direct")) conf += 0.20;
            if(tokens.Contains("removal.efficient")) conf += 0.06;
            if(tokens.Contains("face.damage")) conf += 0.02;
            if(tokens.Contains("lethal.setup")) conf += 0.04;
            return Math.Max(0.05, Math.Min(1.0, conf));
        }

        private static double EstimateRngProbability(SearchResult result)
        {
            try
            {
                foreach(var a in result.Actions)
                {
                    if(a.ActionType == GameActionType.PlayCard)
                    {
                        if(HSIntel.Engine.Data.DirectDamageProvider.TryGetByIdOrName(a.Card?.CardId, a.Card?.CardName, out var info) && info.IsRng)
                            return 0.5; // coarse indicator that RNG is involved
                    }
                }
            }
            catch { }
            return 0.0;
        }

        private static string SafeName(string? name, string? id, string fallback)
        {
            if(!string.IsNullOrWhiteSpace(name))
                return name!;
            if(!string.IsNullOrWhiteSpace(id))
                return id!;
            return fallback;
        }

        private static bool IsLethal(SearchResult result)
        {
            try
            {
                var opp = result.FinalContext.Board.OpponentHero;
                var hp = (opp.Health ?? 0) + (opp.Armor ?? 0);
                return hp <= 0;
            }
            catch { return false; }
        }

        private static IReadOnlyList<string> InferTokens(GameAction a, bool isLethal)
        {
            var list = new List<string>();
            if(isLethal)
                list.Add("lethal.direct");

            switch(a.ActionType)
            {
                case GameActionType.Attack:
                case GameActionType.HeroAttack:
                    if(a.Target != null && a.Target.IsHero)
                        list.Add("face.damage");
                    else
                        list.Add("trade.clean");
                    break;
                case GameActionType.PlayCard:
                    if(a.RequiresTarget && a.Target != null && !a.Target.IsHero)
                        list.Add("removal.efficient");
                    else
                        list.Add("tempo.gain");
                    list.Add("curve.mana");
                    break;
                case GameActionType.HeroPower:
                    list.Add("tempo.gain");
                    break;
                case GameActionType.EndTurn:
                    if(isLethal)
                        list.Add("lethal.setup");
                    else
                        list.Add("setup.synergy");
                    break;
            }
            return list;
        }

        private static string MediumFor(IReadOnlyList<string> tokens)
        {
            if(tokens.Contains("lethal.direct")) return "This line deals lethal damage now.";
            if(tokens.Contains("removal.efficient")) return "Uses efficient removal to clear a threat.";
            if(tokens.Contains("trade.clean")) return "Takes the cleanest trade available.";
            if(tokens.Contains("face.damage")) return "Pushes safe damage to face.";
            if(tokens.Contains("tempo.gain")) return "Maximizes tempo and board presence.";
            if(tokens.Contains("curve.mana")) return "Spends mana efficiently this turn.";
            if(tokens.Contains("setup.synergy")) return "Sets up a synergy for later.";
            if(tokens.Contains("lethal.setup")) return "Sets up a likely lethal next turn.";
            return "Solid, balanced play for position.";
        }

        private static string FullFor(IReadOnlyList<string> tokens)
        {
            if(tokens.Contains("lethal.direct")) return "The sequence accumulates enough damage to reduce the opponent to 0 this turn, considering eligible attackers and modifiers.";
            if(tokens.Contains("removal.efficient")) return "Chooses removal with the best cost-to-impact ratio and respects follow-up curve.";
            if(tokens.Contains("trade.clean")) return "Removes the highest-threat enemy with minimal resource loss and no overkill waste.";
            if(tokens.Contains("face.damage")) return "Transfers spare damage to the enemy hero while maintaining a stable board state.";
            if(tokens.Contains("tempo.gain")) return "Prioritizes plays that add the most stats to board now while preserving options next turn.";
            if(tokens.Contains("curve.mana")) return "Sequences to minimize floating mana and keep next turn flexible.";
            if(tokens.Contains("setup.synergy")) return "Plays enablers before payoffs to maximize value across turns.";
            if(tokens.Contains("lethal.setup")) return "Positions board and resources to enable lethal next turn, barring strong disruption.";
            return "Balances board, hand, and resources for the best overall position.";
        }

        private static double NormalizeScore(double score)
        {
            // Map arbitrary score to [0,1] with diminishing returns
            var s = Math.Max(-50, Math.Min(50, score));
            return 0.5 + Math.Tanh(s / 20.0) * 0.5; // ~0..1
        }
    }

    // Internal DTOs used by RecommendationService; overlay reads them via reflection.
    internal sealed class ExplanationSet
    {
        public ExplanationSet(IReadOnlyList<ExplanationEntry> entries, bool isLethal, double overallConfidence, double rngProbability, DateTimeOffset generatedAt)
        {
            Entries = entries?.ToList() ?? new List<ExplanationEntry>();
            IsLethal = isLethal;
            OverallConfidence = overallConfidence;
            RngProbability = Math.Max(0, Math.Min(1, rngProbability));
            GeneratedAt = generatedAt;
        }
        public IReadOnlyList<ExplanationEntry> Entries { get; private set; }
        public bool IsLethal { get; private set; }
        public double OverallConfidence { get; private set; }
        public double RngProbability { get; private set; }
        public DateTimeOffset GeneratedAt { get; private set; }
    }

    internal sealed class ExplanationEntry
    {
        public ExplanationEntry(int stepIndex, IReadOnlyList<string> tokens, string medium, string full, double confidence)
        {
            StepIndex = stepIndex;
            Tokens = tokens?.ToArray() ?? Array.Empty<string>();
            Medium = medium ?? string.Empty;
            Full = full ?? string.Empty;
            Confidence = Math.Max(0, Math.Min(1, confidence));
        }
        public int StepIndex { get; private set; }
        public string[] Tokens { get; private set; }
        public string Medium { get; private set; }
        public string Full { get; private set; }
        public double Confidence { get; private set; }
    }
}
