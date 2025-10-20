using System;

namespace HSIntel.Core.Config
{
    public class HSIntelConfig
    {
        // Search parameters
        public int BeamWidth { get; set; } = 10;
        public int Depth { get; set; } = 5;
        public int MaxComputeMs { get; set; } = 250;

        // Rollout triggers
        public bool EnableRollouts { get; set; } = false;
        public int RolloutSamples { get; set; } = 64;

        // UI settings
        public double HudLeftPct { get; set; } = 90.0;
        public double HudTopPct { get; set; } = 90.0;
        public bool CoachEnabled { get; set; } = true;

        // Diagnostics
        // When enabled, overlay draws simple markers and emits RenderTick logs.
        public bool DebugOverlayMode { get; set; } = false;

        // Data sources
        public bool PreferHdtData { get; set; } = true;
        public bool EnableHearthstoneJsonFallback { get; set; } = true;
    }
}
