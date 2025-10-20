using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HSIntel.Core.Models;
using HSIntel.Core.Models.Events;

namespace HSIntel.Engine.Secrets
{
    /// <summary>
    /// Stub-only secret summarizer and probe recommender. Logs only.
    /// </summary>
    internal static class SecretHandler
    {
        public static void SummarizeOpponentSecrets(GameContext context)
        {
            if(context?.Board?.Secrets == null)
                return;

            var opponentSecrets = context.Board.Secrets.Where(s => s.Controller == ParticipantSide.Opponent).ToList();
            if(opponentSecrets.Count == 0)
                return;

            var cards = opponentSecrets.Select(s => string.IsNullOrWhiteSpace(s.CardName) ? (s.CardId ?? "Secret") : s.CardName!).ToList();
            Trace.WriteLine($"[HSIntel][Engine] Secrets: opponent has {opponentSecrets.Count} in play => {string.Join(", ", cards)}");
        }

        public static void EmitProbeOrder(GameContext context)
        {
            if(context?.Board?.Secrets == null)
                return;
            var count = context.Board.Secrets.Count(s => s.Controller == ParticipantSide.Opponent);
            if(count == 0)
                return;

            var probes = new List<string>
            {
                "1) Attack face with lowest-attack minion (safe trigger check)",
                "2) Trade a small minion into a minion (board-trigger check)",
                "3) Play the lowest-cost non-critical spell (spell-trigger check)",
                "4) Summon a small minion (summon-trigger check)",
                "5) Proceed if no triggers"
            };

            Trace.WriteLine($"[HSIntel][Engine] Secrets: probe-order start ({count} secrets)");
            foreach(var p in probes) Trace.WriteLine($"[HSIntel][Engine] Secrets: {p}");
            Trace.WriteLine("[HSIntel][Engine] Secrets: probe-order end");
        }
    }
}

