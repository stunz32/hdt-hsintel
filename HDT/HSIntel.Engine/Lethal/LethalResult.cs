using System.Collections.Generic;
using HSIntel.Engine.Models;

namespace HSIntel.Engine.Lethal
{
    /// <summary>
    /// Summary of a minimal lethal plan used as a hint to the search.
    /// </summary>
    public sealed class LethalResult
    {
        public LethalResult(IReadOnlyList<GameAction> actions, int totalDamage, int requiredMana)
        {
            Actions = actions ?? new List<GameAction>();
            TotalDamage = totalDamage;
            RequiredMana = requiredMana;
        }

        public IReadOnlyList<GameAction> Actions { get; }
        public int TotalDamage { get; }
        public int RequiredMana { get; }
    }
}

