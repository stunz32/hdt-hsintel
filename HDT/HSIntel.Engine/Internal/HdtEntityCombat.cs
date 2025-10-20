using System;
using System.Linq;
using System.Reflection;

namespace HSIntel.Engine.Internal
{
    // Internal reflection bridge to HDT Entity tags for attack eligibility.
    // No new references; works on net472 with HDT loaded in-proc.
    internal static class HdtEntityCombat
    {
        private static bool _inited;
        private static Type _coreType;
        private static PropertyInfo _coreGameProp;
        private static Type _gameType;
        private static PropertyInfo _entitiesProp;
        private static Type _entityType;
        private static MethodInfo _getTagMethod;
        private static MethodInfo _hasTagMethod;
        private static Type _gameTagEnum;

        private static void EnsureInit()
        {
            if(_inited) return;
            try
            {
                var asmHdt = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Hearthstone Deck Tracker");
                var asmHearthDb = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "HearthDb");
                if(asmHdt == null || asmHearthDb == null)
                    return;
                _coreType = asmHdt.GetType("Hearthstone_Deck_Tracker.Core");
                _coreGameProp = _coreType?.GetProperty("Game", BindingFlags.Public | BindingFlags.Static);
                _gameType = asmHdt.GetType("Hearthstone_Deck_Tracker.Hearthstone.GameV2");
                _entitiesProp = _gameType?.GetProperty("Entities", BindingFlags.Public | BindingFlags.Instance);
                _entityType = asmHdt.GetType("Hearthstone_Deck_Tracker.Hearthstone.Entities.Entity");
                _getTagMethod = _entityType?.GetMethod("GetTag", new[] { asmHearthDb.GetType("HearthDb.Enums.GameTag") });
                _hasTagMethod = _entityType?.GetMethod("HasTag", new[] { asmHearthDb.GetType("HearthDb.Enums.GameTag") });
                _gameTagEnum = asmHearthDb.GetType("HearthDb.Enums.GameTag");
            }
            catch { }
            finally { _inited = true; }
        }

        // Returns remaining swings for this turn considering Windfury/MegaWindfury and NUM_ATTACKS_THIS_TURN.
        internal static int GetAttacksRemaining(int entityId)
        {
            try
            {
                var e = GetEntity(entityId);
                if(e == null) return 0;
                var perTurn = HasTag(e, "MEGA_WINDFURY") ? 4 : HasTag(e, "WINDFURY") ? 2 : 1;
                var used = GetTag(e, "NUM_ATTACKS_THIS_TURN");
                var remaining = Math.Max(0, perTurn - Math.Max(0, used));
                return remaining;
            }
            catch { return 0; }
        }

        internal static bool CanAttackMinionsNow(int entityId)
        {
            try
            {
                var e = GetEntity(entityId);
                if(e == null) return false;
                if(HasAny(e, "DORMANT", "CANT_ATTACK") || GetAttacksRemaining(entityId) <= 0)
                    return false;
                if(HasTag(e, "FROZEN")) return false;

                var turnsInPlay = GetTag(e, "NUM_TURNS_IN_PLAY");
                var exhausted = GetTag(e, "EXHAUSTED") == 1;
                var hasCharge = HasTag(e, "CHARGE");
                var hasRush = HasTag(e, "RUSH");

                if(turnsInPlay > 0)
                    return !exhausted || hasCharge; // normal case

                // Summoned this turn: Rush allows minion attacks; Charge allows both (handled in face method)
                if(hasRush) return true;
                if(hasCharge) return true;
                return false;
            }
            catch { return false; }
        }

        internal static bool CanAttackFaceNow(int entityId)
        {
            try
            {
                var e = GetEntity(entityId);
                if(e == null) return false;
                if(HasAny(e, "DORMANT", "CANT_ATTACK") || GetAttacksRemaining(entityId) <= 0)
                    return false;
                if(HasTag(e, "FROZEN")) return false;

                var turnsInPlay = GetTag(e, "NUM_TURNS_IN_PLAY");
                var exhausted = GetTag(e, "EXHAUSTED") == 1;
                var hasCharge = HasTag(e, "CHARGE");

                if(turnsInPlay > 0)
                    return !exhausted || hasCharge;

                // Summoned this turn: can attack face only with Charge
                return hasCharge;
            }
            catch { return false; }
        }

        private static object GetEntity(int entityId)
        {
            EnsureInit();
            if(_coreGameProp == null || _entitiesProp == null || _entityType == null) return null;
            var game = _coreGameProp.GetValue(null, null);
            var dict = _entitiesProp.GetValue(game);
            if(dict == null) return null;
            var dictType = dict.GetType();
            var miTryGet = dictType.GetMethod("TryGetValue", new[] { typeof(int), _entityType.MakeByRefType() });
            var args = new object[] { entityId, null };
            var ok = (bool)(miTryGet?.Invoke(dict, args) ?? false);
            if(!ok) return null;
            return args[1];
        }

        private static int GetTag(object entity, string tagName)
        {
            EnsureInit();
            if(entity == null || _getTagMethod == null || _gameTagEnum == null) return 0;
            var tag = Enum.Parse(_gameTagEnum, tagName);
            var val = _getTagMethod.Invoke(entity, new[] { tag });
            return val is int i ? i : 0;
        }

        private static bool HasTag(object entity, string tagName)
        {
            EnsureInit();
            if(entity == null || _hasTagMethod == null || _gameTagEnum == null) return false;
            var tag = Enum.Parse(_gameTagEnum, tagName);
            var val = _hasTagMethod.Invoke(entity, new[] { tag });
            return val is bool b && b;
        }

        private static bool HasAny(object entity, params string[] tagNames)
        {
            foreach(var name in tagNames)
                if(HasTag(entity, name)) return true;
            return false;
        }
    }
}

