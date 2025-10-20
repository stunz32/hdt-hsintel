using System;
using System.Collections.Generic;

namespace HSIntel.Engine.Data
{
    /// <summary>
    /// Minimal curated sets for sequencing rules. Ordering hints only.
    /// </summary>
    internal static class SequencingProvider
    {
        private static HashSet<string>? _drawFirst;
        private static HashSet<string>? _buffFirst;

        private static HashSet<string> DrawFirst
        {
            get
            {
                if(_drawFirst != null)
                    return _drawFirst;
                _drawFirst = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    // Classic/core draw spells (examples; ordering hints only)
                    "CS2_023", // Arcane Intellect
                    "CS2_077", // Sprint
                    "DS1_184", // Tracking (discover/filter)
                };
                return _drawFirst;
            }
        }

        private static HashSet<string> BuffFirst
        {
            get
            {
                if(_buffFirst != null)
                    return _buffFirst;
                _buffFirst = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    // Simple buffs applied before summons
                    "CS2_087", // Blessing of Might
                    "CS2_004", // Power Word: Shield (buff; also draws)
                };
                return _buffFirst;
            }
        }

        public static bool IsDrawFirst(string? cardId)
        {
            if(string.IsNullOrWhiteSpace(cardId))
                return false;
            return DrawFirst.Contains(cardId!);
        }

        public static bool IsBuffFirst(string? cardId)
        {
            if(string.IsNullOrWhiteSpace(cardId))
                return false;
            return BuffFirst.Contains(cardId!);
        }
    }
}

