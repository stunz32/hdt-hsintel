                    }

                    _friendlyBoard = fb; _opponentBoard = ob; _friendlyHand = fh;
                    _overlayRect = overlay; _screenRatio = sr; _valid = true;
                    return true;
                }
                catch { return false; }
            }

            public bool TryGetHandSlot(int index0, out Rect rect)
            {
                rect = Rect.Empty;
                if(!_valid || _friendlyHand == null) return false;
                if(index0 < 0 || index0 >= _friendlyHand.Length) return false;
                rect = _friendlyHand[index0];
                return rect.Width > 0 && rect.Height > 0;
            }

            public bool TryGetBoardSlot(int index0, bool friendly, out Rect rect)
            {
                rect = Rect.Empty;
                if(!_valid) return false;
                var arr = friendly ? _friendlyBoard : _opponentBoard;
                if(arr == null) return false;
                if(index0 < 0 || index0 >= arr.Length) return false;
                rect = arr[index0];
                return rect.Width > 0 && rect.Height > 0;
            }

            public bool TryGetContainingRect(Point screenPoint, out Rect rect)
            {
                rect = Rect.Empty;
                if(!_valid) return false;
                IEnumerable<Rect> all = Enumerable.Empty<Rect>();
                if(_friendlyBoard != null) all = all.Concat(_friendlyBoard);
                if(_opponentBoard != null) all = all.Concat(_opponentBoard);
                if(_friendlyHand != null) all = all.Concat(_friendlyHand);
                var hit = all.FirstOrDefault(r => r.Contains(screenPoint));
                if(hit.IsEmpty) return false;
                rect = hit; return true;
            }

            private static bool AlmostEq(Rect a, Rect b)
            {
                return Math.Abs(a.X - b.X) < 0.5 && Math.Abs(a.Y - b.Y) < 0.5 && Math.Abs(a.Width - b.Width) < 0.5 && Math.Abs(a.Height - b.Height) < 0.5;
            }

            private static bool TryGetOverlayScreenRatio(IOverlayCoordinateMapper mapper, out double ratio)
            {
                ratio = 0;
                try
                {
                    var m = mapper.GetType();
                    if(m.FullName == "Hearthstone_Deck_Tracker.HSIntel.HSIntelOverlayCoordinateMapper")
                    {
                        var fld = m.GetField("_overlayWindow", BindingFlags.NonPublic | BindingFlags.Instance);
                        var overlayWindow = fld?.GetValue(mapper);
                        if(overlayWindow != null)
                        {
                            var prop = overlayWindow.GetType().GetProperty("ScreenRatio", BindingFlags.Public | BindingFlags.Instance);
                            if(prop != null)
                            {
                                var val = prop.GetValue(overlayWindow, null);
                                if(val is double d) { ratio = d; return true; }
                            }
                        }
                    }
                }
                catch { }
                return false;
            }

            private static bool TryMakeRegionDrawer(IOverlayCoordinateMapper mapper, out object? drawer, Rect overlayRect, double screenRatio,
                out Func<int,int,bool,Rect> drawBoard, out Func<int,int,bool,Rect> drawHand)
            {
                drawer = null; drawBoard = null; drawHand = null;
                try
                {
                    var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Hearthstone Deck Tracker");
                    if(asm == null) return false;
                    var rdType = asm.GetType("Hearthstone_Deck_Tracker.Utility.RegionDrawer.RegionDrawer");
                    if(rdType == null) return false;
                    var ctor = rdType.GetConstructor(new[] { typeof(double), typeof(double), typeof(double) });
                    if(ctor == null) return false;
                    var tmpDrawer = ctor.Invoke(new object?[] { overlayRect.Height, overlayRect.Width, screenRatio });

                    var drawBoardMi = rdType.GetMethod("DrawBoardCardRegions", BindingFlags.Public | BindingFlags.Instance);
                    var drawHandMi = rdType.GetMethod("DrawHandCardRegions", BindingFlags.Public | BindingFlags.Instance);
                    if(drawBoardMi == null || drawHandMi == null) return false;

                    var drawerObj = tmpDrawer; // capture local, not out param
                    drawBoard = (slots, pos, friendly) => TakePrimaryAndMap(drawBoardMi, drawerObj, overlayRect, new object?[] { slots, pos, friendly, 0, 0 });
                    drawHand = (slots, pos, friendly) => TakePrimaryAndMap(drawHandMi, drawerObj, overlayRect, new object?[] { slots, pos, friendly, null, 0, 0 });
                    drawer = tmpDrawer;
                    return true;
                }
                catch { drawer = null; drawBoard = null; drawHand = null; return false; }
            }

            private static Rect TakePrimaryAndMap(MethodInfo mi, object drawer, Rect overlayRect, object?[] args)
            {
                var res = mi.Invoke(drawer, args) as System.Collections.IEnumerable;
                if(res == null) return Rect.Empty;
                foreach(var item in res)
                {
                    if(item is Rect r)
                        return ToScreenRect(r, overlayRect);
                }
                return Rect.Empty;
            }

            private static Rect ToScreenRect(Rect normalized, Rect overlayRect)
            {
                if(normalized.IsEmpty) return Rect.Empty;
                var left = overlayRect.X + normalized.X * overlayRect.Width;
                var top = overlayRect.Y + normalized.Y * overlayRect.Height;
                var width = normalized.Width * overlayRect.Width;
                var height = normalized.Height * overlayRect.Height;
                return new Rect(left, top, width, height);
            }
        }

        private static class HdtReflect
        {
            private static bool _initialized;
            private static Type? _coreType;
            private static PropertyInfo? _coreGameProp;
            private static Type? _gameType;
            private static PropertyInfo? _entitiesProp;
            private static Type? _entityType;
            private static PropertyInfo? _zonePosProp;
            private static PropertyInfo? _isMinionProp;
            private static PropertyInfo? _isInHandProp;
            private static MethodInfo? _isControlledByMethod;
            private static Type? _playerType;
            private static PropertyInfo? _playerProp;
            private static PropertyInfo? _playerIdProp;
            private static MethodInfo? _dictTryGetValue;
            private static PropertyInfo? _entitiesValuesProp;

            private static void EnsureInit()
            {
                if(_initialized)
                    return;
                try
                {
                    var asm = AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(a => a.GetName().Name == "Hearthstone Deck Tracker");
                    if(asm == null)
                        return;
                    _coreType = asm.GetType("Hearthstone_Deck_Tracker.Core");
                    _coreGameProp = _coreType?.GetProperty("Game", BindingFlags.Public | BindingFlags.Static);
                    _gameType = asm.GetType("Hearthstone_Deck_Tracker.Hearthstone.GameV2");
                    _entitiesProp = _gameType?.GetProperty("Entities", BindingFlags.Public | BindingFlags.Instance);
                    _playerProp = _gameType?.GetProperty("Player", BindingFlags.Public | BindingFlags.Instance);
                    _playerType = asm.GetType("Hearthstone_Deck_Tracker.Hearthstone.Player");
                    _playerIdProp = _playerType?.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);
                    _entityType = asm.GetType("Hearthstone_Deck_Tracker.Hearthstone.Entities.Entity");
                    _zonePosProp = _entityType?.GetProperty("ZonePosition", BindingFlags.Public | BindingFlags.Instance);
                    _isMinionProp = _entityType?.GetProperty("IsMinion", BindingFlags.Public | BindingFlags.Instance);
                    _isInHandProp = _entityType?.GetProperty("IsInHand", BindingFlags.Public | BindingFlags.Instance);
                    _isControlledByMethod = _entityType?.GetMethod("IsControlledBy", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(int) }, null);
                    // Dictionary<int, Entity>.TryGetValue signature
                    var dictType = typeof(System.Collections.Generic.Dictionary<,>).MakeGenericType(typeof(int), _entityType ?? typeof(object));
                    _dictTryGetValue = dictType.GetMethod("TryGetValue", new[] { typeof(int), _entityType.MakeByRefType() });
                    _entitiesValuesProp = dictType.GetProperty("Values");
                }
                catch { }
                finally { _initialized = true; }
            }

            public static bool TryGetMinionSlotAndSide(int entityId, out int position0, out bool isFriendly)
            {
                position0 = 0; isFriendly = false;
                try
                {
                    EnsureInit();
                    if(_coreGameProp == null || _entitiesProp == null || _entityType == null || _zonePosProp == null || _isMinionProp == null || _playerProp == null || _playerIdProp == null || _isControlledByMethod == null || _dictTryGetValue == null)
                        return false;

                    var game = _coreGameProp.GetValue(null, null);
                    if(game == null)
                        return false;
                    var entities = _entitiesProp.GetValue(game);
                    if(entities == null)
                        return false;

                    var args = new object?[] { entityId, null };
                    var found = (bool)(_dictTryGetValue.Invoke(entities, args) ?? false);
                    if(!found)
                        return false;
                    var entity = args[1];
                    if(entity == null)
                        return false;
                    var isMinion = (bool)(_isMinionProp.GetValue(entity, null) ?? false);
                    if(!isMinion)
                        return false;
                    var zonePos1 = (int)(_zonePosProp.GetValue(entity, null) ?? 0);

                    var player = _playerProp.GetValue(game);
                    var playerId = (int)(_playerIdProp.GetValue(player, null) ?? 0);
                    var friendly = (bool)(_isControlledByMethod.Invoke(entity, new object?[] { playerId }) ?? false);

                    position0 = Math.Max(0, Math.Min(6, Math.Max(1, zonePos1) - 1));
                    isFriendly = friendly;
                    return true;
                }
                catch { return false; }
            }

            public static bool TryGetEntityInfo(int entityId, out int zonePosition0, out bool isMinion, out bool isInHand, out bool isFriendly)
            {
                zonePosition0 = 0; isMinion = false; isInHand = false; isFriendly = false;
                try
                {
                    EnsureInit();
                    if(_coreGameProp == null || _entitiesProp == null || _entityType == null || _zonePosProp == null || _playerProp == null || _playerIdProp == null || _isControlledByMethod == null || _dictTryGetValue == null)
                        return false;

                    var game = _coreGameProp.GetValue(null, null);
                    if(game == null)
                        return false;
                    var entities = _entitiesProp.GetValue(game);
                    if(entities == null)
                        return false;

                    var args = new object?[] { entityId, null };
                    var found = (bool)(_dictTryGetValue.Invoke(entities, args) ?? false);
                    if(!found)
                        return false;
                    var entity = args[1];
                    if(entity == null)
                        return false;

                    var zonePos1 = (int)(_zonePosProp.GetValue(entity, null) ?? 0);
                    zonePosition0 = Math.Max(0, Math.Max(1, zonePos1) - 1);

                    isMinion = _isMinionProp != null && (bool)(_isMinionProp.GetValue(entity, null) ?? false);
                    isInHand = _isInHandProp != null && (bool)(_isInHandProp.GetValue(entity, null) ?? false);

                    var player = _playerProp.GetValue(game);
                    var playerId = (int)(_playerIdProp.GetValue(player, null) ?? 0);
                    isFriendly = (bool)(_isControlledByMethod.Invoke(entity, new object?[] { playerId }) ?? false);
                    return true;
                }
                catch { return false; }
            }

            public static bool TryGetCounts(out int friendlyBoard, out int opponentBoard, out int friendlyHand)
            {
                friendlyBoard = opponentBoard = friendlyHand = 0;
                try
                {
                    EnsureInit();
                    if(_coreGameProp == null || _entitiesProp == null || _entityType == null)
                        return false;
                    var game = _coreGameProp.GetValue(null, null);
                    var entities = _entitiesProp.GetValue(game);
                    if(entities == null) return false;
                    var values = _entitiesValuesProp?.GetValue(entities) as System.Collections.IEnumerable;
                    if(values == null) return false;

                    // Resolve player id once
                    int playerId = 0;
                    if(_playerProp != null && _playerIdProp != null)
                    {
                        var player = _playerProp.GetValue(game);
                        playerId = (int)(_playerIdProp.GetValue(player, null) ?? 0);
                    }
                    var isMinionPi = _entityType.GetProperty("IsMinion");
                    var isInPlayMi = _entityType.GetProperty("IsInPlay");
                    var isInHandMi = _entityType.GetProperty("IsInHand");
                    var isControlledBy = _entityType.GetMethod("IsControlledBy", new[] { typeof(int) });
                    foreach(var e in values)
                    {
                        bool isMinion = (bool)(isMinionPi?.GetValue(e, null) ?? false);
                        if(isMinion)
                        {
                            bool inPlay = (bool)(isInPlayMi?.GetValue(e, null) ?? false);
                            if(inPlay)
                            {
                                bool friendly = (bool)(isControlledBy?.Invoke(e, new object?[] { playerId }) ?? false);
                                if(friendly) friendlyBoard++; else opponentBoard++;
                                continue;
                            }
                        }
                        bool inHand = (bool)(isInHandMi?.GetValue(e, null) ?? false);
                        if(inHand)
                        {
                            bool friendly = (bool)(isControlledBy?.Invoke(e, new object?[] { playerId }) ?? false);
                            if(friendly) friendlyHand++;
                        }
                    }
                    return true;
                }
                catch { return false; }
            }
        }
    }
}
