using System;
using System.Collections.Generic;

namespace HSIntel.Engine.Data
{
    internal enum DamageTargetKind
    {
        Face,
        Minion,
        Flex,
        Aoe,
        Random
    }

    internal sealed class DirectDamageInfo
    {
        public DirectDamageInfo(string cardId, int damage, DamageTargetKind target, int manaCost, bool isRng = false)
        {
            CardId = cardId;
            Damage = damage;
            Target = target;
            ManaCost = manaCost;
            IsRng = isRng || target == DamageTargetKind.Random;
        }

        public string CardId { get; }
        public int Damage { get; }
        public DamageTargetKind Target { get; }
        public int ManaCost { get; }
        public bool IsRng { get; }
    }

    /// <summary>
    /// Engine-internal curated allowlist of direct damage spells.
    /// No external deps; IDs sourced from stable classic set.
    /// </summary>
    internal static class DirectDamageProvider
    {
        private static Dictionary<string, DirectDamageInfo>? _byId;
        private static Dictionary<string, DirectDamageInfo>? _byName;

        private static Dictionary<string, DirectDamageInfo> Ensure()
        {
            if(_byId != null)
                return _byId;

            _byId = new Dictionary<string, DirectDamageInfo>(StringComparer.OrdinalIgnoreCase)
            {
                // Mage
                { "CS2_029", new DirectDamageInfo("CS2_029", 6, DamageTargetKind.Flex, 4) }, // Fireball
                { "CS2_024", new DirectDamageInfo("CS2_024", 3, DamageTargetKind.Flex, 2) }, // Frostbolt (ignore Freeze)
                { "EX1_277", new DirectDamageInfo("EX1_277", 3, DamageTargetKind.Random, 1, isRng: true) }, // Arcane Missiles (3 total, random)

                // Shaman
                { "EX1_238", new DirectDamageInfo("EX1_238", 3, DamageTargetKind.Flex, 1) }, // Lightning Bolt (ignore Overload)
                { "CS2_037", new DirectDamageInfo("CS2_037", 1, DamageTargetKind.Flex, 1) }, // Frost Shock (ignore Freeze)

                // Paladin (AoE includes face damage here as 2)
                { "CS2_093", new DirectDamageInfo("CS2_093", 2, DamageTargetKind.Aoe, 4) }, // Consecration

                // Hunter
                { "DS1_185", new DirectDamageInfo("DS1_185", 2, DamageTargetKind.Flex, 1) }, // Arcane Shot
                { "EX1_539", new DirectDamageInfo("EX1_539", 3, DamageTargetKind.Flex, 3) }, // Kill Command (base only)
            };

            // Optional name-based lookups for common cards where IDs vary across sets.
            // This is purely a fallback and should be replaced by proper card DB in later phases.
            _byName = new Dictionary<string, DirectDamageInfo>(StringComparer.OrdinalIgnoreCase)
            {
                { "Arcane Shot", new DirectDamageInfo("DS1_185", 2, DamageTargetKind.Flex, 1) },
                { "Quick Shot", new DirectDamageInfo("AT_059", 3, DamageTargetKind.Flex, 2) }, // hand-empty draw effect ignored
                { "Wound Prey", new DirectDamageInfo("BAR_801", 1, DamageTargetKind.Flex, 1) }, // token ignored
                { "Fireball", new DirectDamageInfo("CS2_029", 6, DamageTargetKind.Flex, 4) },
            };

            return _byId;
        }

        public static bool TryGet(string? cardId, out DirectDamageInfo info)
        {
            info = null;
            if(string.IsNullOrWhiteSpace(cardId))
                return false;
            return Ensure().TryGetValue(cardId!, out info);
        }

        public static bool TryGetByIdOrName(string? cardId, string? cardName, out DirectDamageInfo info)
        {
            info = null;
            var dict = Ensure();
            if(!string.IsNullOrWhiteSpace(cardId) && dict.TryGetValue(cardId!, out info))
                return true;
            if(!string.IsNullOrWhiteSpace(cardName) && _byName != null)
                return _byName.TryGetValue(cardName!.Trim(), out info);
            return false;
        }

        public static IEnumerable<DirectDamageInfo> EnumerateAll()
        {
            return Ensure().Values;
        }
    }
}
