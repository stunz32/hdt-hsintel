using System;
using System.Collections.Generic;
using System.Diagnostics;
using HSIntel.Core.Models;

namespace HSIntel.Engine.Data
{
    internal sealed class LethalProbability
    {
        public float Probability { get; set; }
        public int SamplesRun { get; set; }
    }

    internal interface IRandomResolver
    {
        string CardId { get; }
        // Compute probability that the effect alone kills the opponent hero from the current context.
        LethalProbability ComputeLethalProbability(GameContext context, int samples, int timeBudgetMs);
    }

    internal static class RandomResolverRegistry
    {
        private static Dictionary<string, IRandomResolver>? _resolvers;

        private static Dictionary<string, IRandomResolver> Ensure()
        {
            if(_resolvers != null)
                return _resolvers;

            _resolvers = new Dictionary<string, IRandomResolver>(StringComparer.OrdinalIgnoreCase)
            {
                { "EX1_277", new ArcaneMissilesResolver() },
            };
            return _resolvers;
        }

        public static bool TryGet(string cardId, out IRandomResolver resolver)
        {
            return Ensure().TryGetValue(cardId, out resolver);
        }
    }

    internal sealed class ArcaneMissilesResolver : IRandomResolver
    {
        public string CardId => "EX1_277"; // Arcane Missiles

        public LethalProbability ComputeLethalProbability(GameContext context, int samples, int timeBudgetMs)
        {
            var sw = Stopwatch.StartNew();
            int lethalCount = 0;
            var rng = new Random(1337);

            // Count current valid targets: all enemy minions + enemy hero
            var targetCountBase = (context.Board?.OpponentMinions?.Count ?? 0) + 1;
            if(targetCountBase <= 0)
                targetCountBase = 1;

            var oppHealth = Math.Max(0, (context.Board?.OpponentHero?.Health ?? 0) + (context.Board?.OpponentHero?.Armor ?? 0));
            for(int i = 0; i < samples; i++)
            {
                if(sw.ElapsedMilliseconds > timeBudgetMs)
                    break;

                // Simulate 3 missiles; each chooses a random enemy character uniformly
                int faceDamage = 0;
                int targetCount = targetCountBase;
                // Track minion HPs crudely to allow removal as targets
                var minionHp = new List<int>();
                foreach(var m in context.Board.OpponentMinions)
                    minionHp.Add(Math.Max(1, m.Health ?? 1));

                for(int j = 0; j < 3; j++)
                {
                    int roll = rng.Next(targetCount); // 0..targetCount-1; index 0 reserved for face
                    if(roll == 0)
                    {
                        faceDamage++;
                    }
                    else
                    {
                        int minionIndex = roll - 1;
                        if(minionIndex >= 0 && minionIndex < minionHp.Count)
                        {
                            minionHp[minionIndex] -= 1;
                            if(minionHp[minionIndex] <= 0)
                            {
                                minionHp.RemoveAt(minionIndex);
                                targetCount = 1 + minionHp.Count;
                            }
                        }
                        else
                        {
                            // Defensive: if indexing goes odd, count as face
                            faceDamage++;
                        }
                    }
                }

                if(faceDamage >= oppHealth)
                    lethalCount++;
            }

            var ran = Math.Max(1, Math.Min(samples, (int)sw.ElapsedMilliseconds == 0 ? samples : samples));
            return new LethalProbability
            {
                Probability = ran > 0 ? (float)lethalCount / ran : 0f,
                SamplesRun = ran
            };
        }
    }
}

