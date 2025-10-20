using System;
using System.Collections.Generic;

namespace HSIntel.Engine.Data
{
    internal sealed class HeroPowerInfo
    {
        public HeroPowerInfo(string heroClass, string cardId, int damage, DamageTargetKind target, int manaCost = 2)
        {
            HeroClass = heroClass;
            CardId = cardId;
            Damage = damage;
            Target = target;
            ManaCost = manaCost;
        }

        public string HeroClass { get; }
        public string CardId { get; }
        public int Damage { get; }
        public DamageTargetKind Target { get; }
        public int ManaCost { get; }
    }

    /// <summary>
    /// Curated mapping for damage-dealing hero powers. We map by cardId first, then by hero class name.
    /// </summary>
    internal static class HeroPowerProvider
    {
        private static Dictionary<string, HeroPowerInfo>? _byCardId;
        private static Dictionary<string, HeroPowerInfo>? _byHeroClass;

        private static void Ensure()
        {
            if(_byCardId != null && _byHeroClass != null)
                return;

            _byCardId = new Dictionary<string, HeroPowerInfo>(StringComparer.OrdinalIgnoreCase);
            _byHeroClass = new Dictionary<string, HeroPowerInfo>(StringComparer.OrdinalIgnoreCase);

            void Add(HeroPowerInfo i)
            {
                _byCardId![i.CardId] = i;
                if(!string.IsNullOrWhiteSpace(i.HeroClass))
                    _byHeroClass![i.HeroClass] = i;
            }

            // Mage: Fireblast, 1 damage, flexible target
            Add(new HeroPowerInfo("MAGE", "CS2_034", 1, DamageTargetKind.Flex, 2));
            // Hunter: Steady Shot, 2 to face only
            Add(new HeroPowerInfo("HUNTER", "DS1h_292", 2, DamageTargetKind.Face, 2));
            // Others are ignored for this phase (non-damage or self-buff)
        }

        public static bool TryGetByCardId(string? cardId, out HeroPowerInfo info)
        {
            Ensure();
            info = null;
            if(string.IsNullOrWhiteSpace(cardId))
                return false;
            return _byCardId!.TryGetValue(cardId!, out info);
        }

        public static bool TryGetByClass(string? heroClass, out HeroPowerInfo info)
        {
            Ensure();
            info = null;
            if(string.IsNullOrWhiteSpace(heroClass))
                return false;
            return _byHeroClass!.TryGetValue(heroClass!, out info);
        }
    }
}

