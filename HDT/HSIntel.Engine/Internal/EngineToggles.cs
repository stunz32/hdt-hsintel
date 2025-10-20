using System;

namespace HSIntel.Engine.Internal
{
    /// <summary>
    /// Engine-internal feature toggles for Phase 6 functionality.
    /// Defaults are conservative to preserve existing behavior unless explicitly enabled.
    /// </summary>
    internal static class EngineToggles
    {
        // Curated direct-damage spells in simulator/lethal fast-path
        public static bool EnableCuratedSpells { get; set; } = true;

        // Curated hero power damage application in simulator/lethal fast-path
        public static bool EnableCuratedHeroPowerDamage { get; set; } = true;

        // MoveGenerator sequencing enforcement
        public static bool EnableSequencingOrdering { get; set; } = false;

        // Secret risk as ordering-only adjustment
        public static bool EnableSecretRiskOrdering { get; set; } = false;

        // Phase 7: Play recommendation pipeline (mapping SearchResult -> overlay-ready steps)
        // Disabled by default to preserve existing behavior unless explicitly enabled.
        public static bool EnableRecommendationPipeline { get; set; } = true;

        // Phase 7: Opponent reads adjustments in evaluator (ordering/penalty only)
        public static bool EnableOpponentReads { get; set; } = false;

        // Phase 7: Performance logging for recommendations (file log, minimal)
        public static bool EnableRecommendationPerfLogging { get; set; } = false;

        // Phase 7: Auto-disable recommendation pipeline if repeatedly over budget
        public static bool EnableAutoDisableRecommendation { get; set; } = false;

        // Phase 9+: Heuristic: when simulating generic PlayCard without card DB,
        // approximate that many plays add board presence (non-attacking body this turn).
        public static bool EnableHeuristicMinionSummon { get; set; } = true;

        // Phase 9+: Sequencing hint: prefer card plays over hero power when equivalent.
        public static bool EnableHeroPowerAfterPlaysOrdering { get; set; } = true;
    }
}
