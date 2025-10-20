using System;
using System.Windows;
using System.Windows.Controls;
using HSIntel.Core.Config;
using HSIntel.Overlay.Interfaces;
using HSIntel.Overlay.Layers;
using HSIntel.Overlay.Models;
using System.Linq;
using System.Reflection;
using System.Windows.Threading;
using HSIntel.Engine.Models;
using HSIntel.Engine.Services;
using System.Collections.Generic;

namespace HSIntel.Overlay.Controls
{
    public partial class IntelOverlayHost : UserControl
    {
        // Internal QA toggle; keep false by default
        private const bool DebugCrosshairs = false;

        private double _hudLeftPct = 90.0;
        private double _hudTopPct = 90.0;
        private HSIntelConfig? _configSnapshot;
        private IOverlayCoordinateMapper? _coordinateMapper;
        private DispatcherTimer? _recoTimer;
        private string? _lastSignature;
        private ExactSlotCache _slotCache = new ExactSlotCache();
        private bool? _lastIsPlayerTurn;

        public event EventHandler<HudPositionChangedEventArgs>? HudPositionChanged;
        public event EventHandler<bool>? HudEnabledChanged;
        public event EventHandler? HudDragStarted;
        public event EventHandler? HudDragCompleted;

        public IntelOverlayHost()
        {
            InitializeComponent();
            SizeChanged += OnSizeChanged;
            CoachHud.DragRequested += OnHudDragRequested;
            CoachHud.DragStarted += OnHudDragStarted;
            CoachHud.DragCompleted += OnHudDragCompleted;
            CoachHud.CoachEnabledChanged += OnCoachEnabledChanged;
            Unloaded += OnUnloaded;

            // Lightweight polling loop to refresh visuals only when the sequence changes.
            _recoTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _recoTimer.Tick += (_, __) => TryUpdateRecommendationVisuals();
            _recoTimer.Start();

            // Attempt an early draw so guidance appears before the first action each turn.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try { System.Diagnostics.Trace.WriteLine("[HSIntel][Overlay] firstDrawAttempt"); } catch { }
                TryUpdateRecommendationVisuals();
            }), DispatcherPriority.Background);
        }

        public OrdersAdornerLayer Orders => OrdersLayer;

        public ArrowAdornerLayer Arrows => ArrowLayer;

        public HaloAdornerLayer Halo => HaloLayer;

        public CoachHudShell Hud => CoachHud;

        public void ApplyConfiguration(HSIntelConfig config)
        {
            _configSnapshot = config ?? throw new ArgumentNullException(nameof(config));
            _hudLeftPct = _configSnapshot.HudLeftPct;
            _hudTopPct = _configSnapshot.HudTopPct;

            CoachHud.SetConfigSnapshot(_configSnapshot);
            UpdateHudEnabledState(_configSnapshot.CoachEnabled);

            UpdateHudPosition();
        }

        public void SetCoordinateMapper(IOverlayCoordinateMapper mapper)
        {
            _coordinateMapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            CoachHud.CoordinateMapper = _coordinateMapper;
            _coordinateMapper.Refresh();
            _slotCache.Invalidate();
            try { System.Diagnostics.Trace.WriteLine("[HSIntel][Overlay] firstDrawAttempt"); } catch { }
            TryUpdateRecommendationVisuals();
        }

        public void UpdateHudPercentPosition(double leftPercent, double topPercent)
        {
            _hudLeftPct = Math.Max(0, Math.Min(100, leftPercent));
            _hudTopPct = Math.Max(0, Math.Min(100, topPercent));
            HudPositionChanged?.Invoke(this, new HudPositionChangedEventArgs(_hudLeftPct, _hudTopPct));
            UpdateHudPosition();
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs args)
        {
            UpdateHudPosition();
            _coordinateMapper?.Refresh();
            _slotCache.Invalidate();
        }

        private void UpdateHudPosition()
        {
            if(ActualWidth <= 0 || ActualHeight <= 0)
                return;

            var left = Math.Max(0, ActualWidth * (_hudLeftPct / 100.0));
            var top = Math.Max(0, ActualHeight * (_hudTopPct / 100.0));

            Canvas.SetLeft(CoachHud, left);
            Canvas.SetTop(CoachHud, top);
        }

        private void OnHudDragRequested(object? sender, HudDragDeltaEventArgs e)
        {
            var currentLeft = Canvas.GetLeft(CoachHud);
            var currentTop = Canvas.GetTop(CoachHud);

            if(double.IsNaN(currentLeft))
                currentLeft = 0;
            if(double.IsNaN(currentTop))
                currentTop = 0;

            var desiredLeft = currentLeft + e.Delta.X;
            var desiredTop = currentTop + e.Delta.Y;

            desiredLeft = Math.Max(0, Math.Min(ActualWidth - CoachHud.ActualWidth, desiredLeft));
            desiredTop = Math.Max(0, Math.Min(ActualHeight - CoachHud.ActualHeight, desiredTop));

            Canvas.SetLeft(CoachHud, desiredLeft);
            Canvas.SetTop(CoachHud, desiredTop);

            if(ActualWidth <= 0 || ActualHeight <= 0)
                return;

            var leftPct = (desiredLeft / ActualWidth) * 100.0;
            var topPct = (desiredTop / ActualHeight) * 100.0;

            UpdateHudPercentPosition(leftPct, topPct);
        }

        private void OnHudDragStarted(object? sender, EventArgs e)
        {
            HudDragStarted?.Invoke(this, EventArgs.Empty);
        }

        private void OnHudDragCompleted(object? sender, EventArgs e)
        {
            // Snap to edges/corners if near edges to make positioning tidy
            try
            {
                if(ActualWidth > 0 && ActualHeight > 0)
                {
                    var left = Canvas.GetLeft(CoachHud);
                    var top = Canvas.GetTop(CoachHud);
                    if(double.IsNaN(left)) left = 0;
                    if(double.IsNaN(top)) top = 0;

                    var leftPct = (left / ActualWidth) * 100.0;
                    var topPct = (top / ActualHeight) * 100.0;

                    leftPct = SnapPercent(leftPct);
                    topPct = SnapPercent(topPct);

                    UpdateHudPercentPosition(leftPct, topPct);
                }
            }
            catch { }

            HudDragCompleted?.Invoke(this, EventArgs.Empty);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            CoachHud.DragRequested -= OnHudDragRequested;
            CoachHud.DragStarted -= OnHudDragStarted;
            CoachHud.DragCompleted -= OnHudDragCompleted;
            CoachHud.CoachEnabledChanged -= OnCoachEnabledChanged;
            SizeChanged -= OnSizeChanged;
            Unloaded -= OnUnloaded;
            _coordinateMapper = null;
            if(_recoTimer != null)
            {
                try { _recoTimer.Stop(); } catch { }
                _recoTimer = null;
            }
        }

        private void OnCoachEnabledChanged(object? sender, bool isEnabled)
        {
            UpdateHudEnabledState(isEnabled);
            HudEnabledChanged?.Invoke(this, isEnabled);
        }

        private void UpdateHudEnabledState(bool isEnabled)
        {
            CoachHud.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;
            CoachHud.IsHitTestVisible = isEnabled;
        }

        private static double SnapPercent(double pct)
        {
            // Snap to 0% or 100% if within 5% of an edge; otherwise leave unchanged
            if(pct < 5) return 0;
            if(pct > 95) return 100;
            return pct;
        }

        private void TryUpdateRecommendationVisuals()
        {
            try
            {
                if(_coordinateMapper == null)
                {
                    try { System.Diagnostics.Trace.WriteLine("[HSIntel][Overlay] Skip: no mapper"); } catch { }
                    return;
                }

                // Read current engine recommendation; avoid allocations if unchanged.
                var actions = RecommendationService.GetCurrentActionsOrEmpty();
                // Detect friendly turn flips and force a redraw gate reset
                if(HdtReflect.TryIsPlayersTurn(out var isMyTurn))
                {
                    if(_lastIsPlayerTurn.HasValue && _lastIsPlayerTurn.Value != isMyTurn)
                    {
                        _lastSignature = null;
                        try { System.Diagnostics.Trace.WriteLine($"[HSIntel][Overlay] TurnFlip: isPlayerTurn={(isMyTurn ? 1 : 0)} forced"); } catch { }
                    }
                    _lastIsPlayerTurn = isMyTurn;
                }
                if(actions == null || actions.Count == 0)
                {
                    try { System.Diagnostics.Trace.WriteLine("[HSIntel][Overlay] Skip: no actions"); } catch { }
                    if(_lastSignature != null)
                    {
                        _lastSignature = null;
                        Orders.ClearSnapshot();
                        Arrows.ClearSnapshot();
                        Halo.ClearSnapshot();
                    }
                    return;
                }

                var sig = ComputeSignature(actions);
                if(string.Equals(sig, _lastSignature, StringComparison.Ordinal))
                    return;

                _lastSignature = sig;
                try { System.Diagnostics.Trace.WriteLine($"[HSIntel][Overlay] Sig: changed=1 len={sig.Length} source=counts+positions+nonce"); } catch { }
                RedrawFrom(actions);
            }
            catch { }
        }

        private static string ComputeSignature(System.Collections.Generic.IReadOnlyList<GameAction> actions)
        {
            // Compact, stable signature sufficient to detect changes for rendering.
            var parts = new System.Text.StringBuilder();
            for(int i = 0; i < actions.Count; i++)
            {
                var a = actions[i];
                parts.Append((int)a.ActionType).Append(':');

                // Base identity
                parts.Append(a.Card?.EntityId?.ToString() ?? "_").Append(':')
                     .Append(a.Attacker?.EntityId?.ToString() ?? "_").Append(':')
                     .Append(a.Target?.EntityId?.ToString() ?? "_");

                // Geometry hints (positions) so we redraw when slots change even if sequence is same
                if(a.Card?.EntityId is int cId && HdtReflect.TryGetEntityInfo(cId, out var cpos, out var cIsMinion, out var cIsInHand, out var cIsFriendly))
                    parts.Append($"#H{cpos}");
                if(a.Attacker?.EntityId is int aId && HdtReflect.TryGetEntityInfo(aId, out var apos, out var aIsMinion, out var aIsInHand, out var aIsFriendly))
                    parts.Append($"#A{apos}:{(aIsFriendly ? "F" : "O")}");
                if(a.Target?.EntityId is int tId && !a.Target.IsHero && HdtReflect.TryGetEntityInfo(tId, out var tpos, out var tIsMinion, out var tIsInHand, out var tIsFriendly))
                    parts.Append($"#T{tpos}:{(tIsFriendly ? "F" : "O")}");

                parts.Append('|');
            }
            // Include live board/hand counts AND a small nonce (max entity id) to force redraw after spawn/death/shifts
            if(HdtReflect.TryGetCounts(out var fBoard, out var oBoard, out var fHand))
                parts.Append($"@C:{fBoard},{oBoard},{fHand}");
            if(HdtReflect.TryGetBoardHandMaxEntityId(out var maxId))
                parts.Append($"@M:{maxId}");
            return parts.ToString();
        }

        private void RedrawFrom(System.Collections.Generic.IReadOnlyList<GameAction> actions)
        {
            if(_coordinateMapper == null)
                return;

            var mapper = _coordinateMapper;
            mapper.Refresh();

            var overlay = mapper.OverlayBounds;
            var board = mapper.BoardRegion;
            var hand = mapper.PlayerHandRegion;
			// Diagnostics: dump regions, dpi, and ratio source/value
			try
			{
				var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
				double ratioVal;
				string ratioSrc;
				if(ExactSlotCache.TryGetOverlayScreenRatio(mapper, out var r))
				{
					ratioVal = r; ratioSrc = "OverlayWindow";
				}
				else
				{
					var aspect = overlay.Width / Math.Max(1.0, overlay.Height);
					ratioVal = (4.0 / 3.0) / Math.Max(1e-6, aspect);
					ratioSrc = "Fallback";
				}
				System.Diagnostics.Trace.WriteLine($"[HSIntel][Overlay] Check: overlay=({overlay.X},{overlay.Y},{overlay.Width},{overlay.Height}) board=({board.X},{board.Y},{board.Width},{board.Height}) hand=({hand.X},{hand.Y},{hand.Width},{hand.Height}) dpi=({dpi.DpiScaleX*96:0.#},{dpi.DpiScaleY*96:0.#}) ratio={ratioVal:0.######} src={ratioSrc}");
			}
			catch { }
            if(board.Width <= 0 || board.Height <= 0 || hand.Width <= 0 || hand.Height <= 0)
            {
                Orders.ClearSnapshot();
                Arrows.ClearSnapshot();
                Halo.ClearSnapshot();
                return;
            }

            // Recompute exact slot rectangles if size/ratio changed
            var ensured = _slotCache.TryEnsure(mapper);
            try
            {
                if(ensured)
                {
                    double sr;
                    var has = ExactSlotCache.TryGetOverlayScreenRatio(mapper, out sr);
                    var used = has ? sr : ((4.0 / 3.0) / Math.Max(1e-6, overlay.Width / Math.Max(1.0, overlay.Height)));
                    System.Diagnostics.Trace.WriteLine($"[HSIntel][Overlay] SlotCache: ok ratio={used:0.######} ratioSource={(has ? "OverlayWindow" : "Fallback")}");
                    if(_slotCache.TryGetHandSlot(2, out var hh)) System.Diagnostics.Trace.WriteLine($"[HSIntel][Overlay] SlotCache: hand[3]={hh}");
                    if(_slotCache.TryGetBoardSlot(3, true, out var bb)) System.Diagnostics.Trace.WriteLine($"[HSIntel][Overlay] SlotCache: boardF[4]={bb}");
                }
                else System.Diagnostics.Trace.WriteLine("[HSIntel][Overlay] SlotCache: failed");
            }
            catch { }

            var orders = new System.Collections.Generic.List<Layers.OrdersAdornerLayer.OrderGlyph>();
            var arrows = new System.Collections.Generic.List<Layers.ArrowAdornerLayer.ArrowGlyph>();
            var halos = new System.Collections.Generic.List<Layers.HaloAdornerLayer.HaloGlyph>();

            for(int i = 0; i < actions.Count; i++)
            {
                var a = actions[i];
                var order = i + 1;

                switch(a.ActionType)
                {
                    case GameActionType.PlayCard:
                    {
                        // Prefer live hand slot by entity id; fall back to provided zone position; then hand center
                        Point srcScreen;
                        double srcRadius = Math.Max(14, hand.Height * 0.045);
                        bool haveSlot = false;
                        if(a.Card?.EntityId is int cId && HdtReflect.TryGetEntityInfo(cId, out var pos0, out var isMinion0, out var isInHand0, out var isFriendly0) && isInHand0 && isFriendly0)
                        {
                            if(_slotCache.TryGetHandSlot(pos0, out var rect))
                            {
                                srcScreen = RectCenter(rect);
                                srcRadius = Math.Max(14, rect.Height * 0.45);
                                haveSlot = true;
                            }
                            else if(TryResolveHandSlotDirect(mapper, pos0, out rect))
                            {
                                srcScreen = RectCenter(rect);
                                srcRadius = Math.Max(14, rect.Height * 0.45);
                                haveSlot = true;
                            }
                            else srcScreen = SlotCenter(hand, Math.Max(0, Math.Min(9, pos0)), 10, verticalBias: -0.35);
                        }
                        else if(a.Card?.ZonePosition is int slot1)
                        {
                            var slot0 = Math.Max(0, Math.Min(9, slot1));
                            if(_slotCache.TryGetHandSlot(slot0, out var rect))
                            {
                                srcScreen = RectCenter(rect);
                                srcRadius = Math.Max(14, rect.Height * 0.45);
                                haveSlot = true;
                            }
                            else if(TryResolveHandSlotDirect(mapper, slot0, out rect))
                            {
                                srcScreen = RectCenter(rect);
                                srcRadius = Math.Max(14, rect.Height * 0.45);
                                haveSlot = true;
                            }
                            else srcScreen = SlotCenter(hand, slot0, 10, verticalBias: -0.35);
                        }
                        else
                        {
                            srcScreen = SlotCenter(hand, 4, 10, verticalBias: -0.35);
                        }

                        var src = ToLocal(mapper, srcScreen);
                        // If we had an exact rect, place the badge at the card's top-center for precise alignment
                        if(haveSlot && _slotCache.TryGetContainingRect(srcScreen, out var exactRect))
                        {
                            var topCenter = new Point(exactRect.X + exactRect.Width * 0.5, exactRect.Y - Math.Max(6, exactRect.Height * 0.015));
                            var badge = ToLocal(mapper, topCenter);
                            orders.Add(new Layers.OrdersAdornerLayer.OrderGlyph(badge, order));
                        }
                        else
                        {
                            var badgeDy = -Math.Max(12, srcRadius * 0.35);
                            orders.Add(new Layers.OrdersAdornerLayer.OrderGlyph(Offset(src, 0, badgeDy), order));
                        }
                        if(DebugCrosshairs) halos.Add(new Layers.HaloAdornerLayer.HaloGlyph(src, Math.Max(4, srcRadius * 0.25), true));
                        if(a.RequiresTarget && a.Target != null)
                        {
                            var tgtScreen = ResolveTargetPointExact(mapper, a.Target, out var tgtRect, out var _);
                            var tgt = ToLocal(mapper, tgtScreen);
                            arrows.Add(new Layers.ArrowAdornerLayer.ArrowGlyph(src, tgt));
                        }
                        if(i == 0)
                        {
                            try { System.Diagnostics.Trace.WriteLine($"[HSIntel][Overlay] A0: type=PlayCard id={a.Card?.EntityId?.ToString() ?? "_"} using={(haveSlot ? "ExactHandSlot" : "FallbackHand")}"); } catch { }
                        }
                        break;
                    }
                    case GameActionType.Attack:
                    {
                        // Attacker is a friendly minion or hero
                        Point srcScreen;
                        double srcR;
                        bool usedExactSlot = false;
                        if(a.Attacker?.EntityId is int aId && HdtReflect.TryGetMinionSlotAndSide(aId, out var apos0, out var afriendly) && (_slotCache.TryGetBoardSlot(Math.Max(0, apos0), afriendly, out var srect) || TryResolveBoardSlotDirect(mapper, Math.Max(0, apos0), afriendly, out srect)))
                        {
                            srcScreen = RectCenter(srect);
                            srcR = Math.Max(18, srect.Height * 0.6);
                            usedExactSlot = true;
                        }
                        else
                        {
                            if(a.Attacker != null && (_slotCache.TryGetBoardSlot(Math.Max(0, a.Attacker.Position), true, out var srect2) || TryResolveBoardSlotDirect(mapper, Math.Max(0, a.Attacker.Position), true, out srect2)))
                            {
                                srcScreen = RectCenter(srect2);
                                srcR = Math.Max(18, srect2.Height * 0.6);
                                usedExactSlot = true;
                            }
                            else
                            {
                                srcScreen = FriendlyHeroPoint(board);
                                srcR = Math.Max(18, board.Height * 0.06);
                            }
                        }

                        var tgtScreen = a.Target != null ? ResolveTargetPointExact(mapper, a.Target, out var trect, out var isFriendlyTgt) : OpponentHeroPoint(board);

                        var src = ToLocal(mapper, srcScreen);
                        var tgt = ToLocal(mapper, tgtScreen);

                        orders.Add(new Layers.OrdersAdornerLayer.OrderGlyph(Offset(src, 0, -Math.Max(14, srcR * 0.35)), order));
                        arrows.Add(new Layers.ArrowAdornerLayer.ArrowGlyph(src, tgt));
                        var tgtR = _slotCache.TryGetContainingRect(tgtScreen, out var trect2) ? Math.Max(18, trect2.Height * 0.6) : Math.Max(18, board.Height * 0.06);
                        halos.Add(new Layers.HaloAdornerLayer.HaloGlyph(src, srcR, true));
                        halos.Add(new Layers.HaloAdornerLayer.HaloGlyph(tgt, tgtR, false));
                        if(i == 0)
                        {
                            try { System.Diagnostics.Trace.WriteLine($"[HSIntel][Overlay] A0: type=Attack id={a.Attacker?.EntityId?.ToString() ?? "_"} using={(usedExactSlot ? "ExactBoardSlot" : "FallbackBoard")}"); } catch { }
                        }
                        if(DebugCrosshairs)
                        {
                            halos.Add(new Layers.HaloAdornerLayer.HaloGlyph(src, Math.Max(4, srcR * 0.2), true));
                            halos.Add(new Layers.HaloAdornerLayer.HaloGlyph(tgt, Math.Max(4, tgtR * 0.2), false));
                        }
                        break;
                    }
                    case GameActionType.HeroAttack:
                    {
                        var srcScreen = FriendlyHeroPoint(board);
                        var tgtScreen = a.Target != null ? ResolveTargetPointExact(mapper, a.Target, out var trect3, out var _isFriendly3) : OpponentHeroPoint(board);
                        var src = ToLocal(mapper, srcScreen);
                        var tgt = ToLocal(mapper, tgtScreen);
                        var srcR = Math.Max(18, board.Height * 0.06);
                        var tgtR = _slotCache.TryGetContainingRect(tgtScreen, out var trect4) ? Math.Max(18, trect4.Height * 0.6) : Math.Max(18, board.Height * 0.06);
                        orders.Add(new Layers.OrdersAdornerLayer.OrderGlyph(Offset(src, 0, -Math.Max(14, srcR * 0.35)), order));
                        arrows.Add(new Layers.ArrowAdornerLayer.ArrowGlyph(src, tgt));
                        halos.Add(new Layers.HaloAdornerLayer.HaloGlyph(src, srcR, true));
                        halos.Add(new Layers.HaloAdornerLayer.HaloGlyph(tgt, tgtR, false));
                        if(DebugCrosshairs)
                        {
                            halos.Add(new Layers.HaloAdornerLayer.HaloGlyph(src, Math.Max(4, srcR * 0.2), true));
                            halos.Add(new Layers.HaloAdornerLayer.HaloGlyph(tgt, Math.Max(4, tgtR * 0.2), false));
                        }
                        break;
                    }
                    case GameActionType.HeroPower:
                    {
                        Point srcScreen;
                        double badgeDy;
                        if(_slotCache.TryGetHeroPowerRect(true, out var hpRect) && hpRect.Height > 0)
                        {
                            srcScreen = RectCenter(hpRect);
                            // Scale badge offset with control height for ratio-proof placement
                            badgeDy = -Math.Max(12, hpRect.Height * 0.25);
                        }
                        else
                        {
                            srcScreen = HeroPowerPoint(board);
                            badgeDy = -Math.Max(12, board.Height * 0.04);
                        }
                        var src = ToLocal(mapper, srcScreen);
                        orders.Add(new Layers.OrdersAdornerLayer.OrderGlyph(Offset(src, 0, badgeDy), order));
                        if(a.RequiresTarget && a.Target != null)
                        {
                            var tgt = ToLocal(mapper, ResolveTargetPoint(mapper, a.Target));
                            arrows.Add(new Layers.ArrowAdornerLayer.ArrowGlyph(src, tgt));
                        }
                        break;
                    }
                    case GameActionType.EndTurn:
                    {
                        var endP = EndTurnButtonPoint(board);
                        var p = ToLocal(mapper, endP);
                        orders.Add(new Layers.OrdersAdornerLayer.OrderGlyph(p, order));
                        break;
                    }
                }
            }

            Orders.SetOrders(orders.Cast<object>());
            Arrows.SetArrows(arrows.Cast<object>());
            Halo.SetHalos(halos.Cast<object>());

            try
            {
                System.Diagnostics.Trace.WriteLine($"[HSIntel][Overlay] Drew: orders={orders.Count} arrows={arrows.Count} halos={halos.Count}");
            }
            catch { }
        }

        private static Point RectCenter(Rect r) => new Point(r.X + r.Width * 0.5, r.Y + r.Height * 0.5);

        private Point ResolveTargetPointExact(IOverlayCoordinateMapper mapper, ActionTarget target, out Rect targetRect, out bool isFriendly)
        {
            var board = mapper.BoardRegion;
            targetRect = Rect.Empty;
            isFriendly = target.Side == HSIntel.Core.Models.Events.ParticipantSide.Friendly;
            if(target.IsHero)
                return isFriendly ? FriendlyHeroPoint(board) : OpponentHeroPoint(board);

            if(target.EntityId.HasValue)
            {
                if(HdtReflect.TryGetMinionSlotAndSide(target.EntityId.Value, out var pos0, out var minionFriendly))
                {
                    isFriendly = minionFriendly;
                    if(_slotCache.TryGetBoardSlot(pos0, minionFriendly, out var rect))
                    {
                        targetRect = rect;
                        return RectCenter(rect);
                    }
                    return BoardMinionCenter(board, pos0, minionFriendly);
                }
            }
            return isFriendly ? BoardMinionCenter(board, 3, friendly: true)
                              : BoardMinionCenter(board, 3, friendly: false);
        }

        private static Point ResolveTargetPoint(IOverlayCoordinateMapper mapper, ActionTarget target)
        {
            var board = mapper.BoardRegion;
            if(target.IsHero)
                return target.Side == HSIntel.Core.Models.Events.ParticipantSide.Friendly ? FriendlyHeroPoint(board) : OpponentHeroPoint(board);

            // Minion target: try to resolve exact slot using HDT entity data via reflection.
            if(target.EntityId.HasValue)
            {
                if(HdtReflect.TryGetMinionSlotAndSide(target.EntityId.Value, out var pos0, out var isFriendly))
                    return BoardMinionCenter(board, pos0, isFriendly);
            }
            // Fallback to row center when not resolvable.
            return target.Side == HSIntel.Core.Models.Events.ParticipantSide.Friendly
                ? BoardMinionCenter(board, 3, friendly: true)
                : BoardMinionCenter(board, 3, friendly: false);
        }

        private static Point ToLocal(IOverlayCoordinateMapper mapper, Point screenPoint)
        {
            var overlay = mapper.OverlayBounds;
            return new Point(screenPoint.X - overlay.X, screenPoint.Y - overlay.Y);
        }

        private static Point SlotCenter(Rect region, int index, int slots, double verticalBias)
        {
            index = Math.Max(0, Math.Min(slots - 1, index));
            var cell = region.Width / Math.Max(1, slots);
            var x = region.X + (index + 0.5) * cell;
            var y = region.Y + region.Height * (0.5 + verticalBias);
            return new Point(x, y);
        }

        // Attempt to compute a single exact slot rect via RegionDrawer when cache is invalid
        private bool TryResolveHandSlotDirect(IOverlayCoordinateMapper mapper, int index0, out Rect rect)
        {
            rect = Rect.Empty;
            try
            {
                double sr;
                var has = ExactSlotCache.TryGetOverlayScreenRatio(mapper, out sr);
                if(!has)
                {
                    var overlay = mapper.OverlayBounds;
                    var aspect = overlay.Width / Math.Max(1.0, overlay.Height);
                    sr = (4.0 / 3.0) / Math.Max(1e-6, aspect);
                }
                if(ExactSlotCache.TryMakeRegionDrawer(mapper, out var _drawer, mapper.OverlayBounds, sr, out var drawBoard, out var drawHand, out var _drawHero))
                {
                    var r = drawHand(10, index0 + 1, true);
                    if(r.Width > 0 && r.Height > 0) { rect = r; return true; }
                }
            }
            catch { }
            return false;
        }

        private bool TryResolveBoardSlotDirect(IOverlayCoordinateMapper mapper, int index0, bool friendly, out Rect rect)
        {
            rect = Rect.Empty;
            try
            {
                double sr;
                var has = ExactSlotCache.TryGetOverlayScreenRatio(mapper, out sr);
                if(!has)
                {
                    var overlay = mapper.OverlayBounds;
                    var aspect = overlay.Width / Math.Max(1.0, overlay.Height);
                    sr = (4.0 / 3.0) / Math.Max(1e-6, aspect);
                }
                if(ExactSlotCache.TryMakeRegionDrawer(mapper, out var _drawer, mapper.OverlayBounds, sr, out var drawBoard, out var drawHand, out var _drawHero))
                {
                    var r = drawBoard(7, index0 + 1, friendly);
                    if(r.Width > 0 && r.Height > 0) { rect = r; return true; }
                }
            }
            catch { }
            return false;
        }

        private static Point BoardMinionCenter(Rect board, int position, bool friendly)
        {
            var slots = 7;
            // Tuned row anchors for our mapper: friendly ~78%, opponent ~22% of board height.
            var y = friendly ? board.Y + board.Height * 0.78 : board.Y + board.Height * 0.22;
            var cell = board.Width / slots;
            var x = board.X + (Math.Max(0, Math.Min(slots - 1, position)) + 0.5) * cell;
            return new Point(x, y);
        }

        private static Point FriendlyHeroPoint(Rect board) => new Point(board.X + board.Width * 0.50, board.Y + board.Height * 0.88);
        private static Point OpponentHeroPoint(Rect board) => new Point(board.X + board.Width * 0.50, board.Y + board.Height * 0.12);
        private static Point HeroPowerPoint(Rect board) => new Point(board.X + board.Width * 0.62, board.Y + board.Height * 0.82);
        private static Point EndTurnButtonPoint(Rect board) => new Point(board.X + board.Width * 0.93, board.Y + board.Height * 0.13);

        private static Point Offset(Point p, double dx, double dy) => new Point(p.X + dx, p.Y + dy);

        // Caches exact slot rectangles computed via HDT RegionDrawer, mapped to screen space
        private sealed class ExactSlotCache
        {
            private Rect[]? _friendlyBoard; // 7
            private Rect[]? _opponentBoard; // 7
            private Rect[]? _friendlyHand;  // 10
            private Rect _friendlyHeroPower;
            private Rect _overlayRect;
            private double _screenRatio;
            private bool _valid;

            public void Invalidate() { _valid = false; }

            public bool TryEnsure(IOverlayCoordinateMapper mapper)
            {
                try
                {
                    var overlay = mapper.OverlayBounds;
                    if(overlay.Width <= 0 || overlay.Height <= 0)
                        return false;

                    var sr = TryGetOverlayScreenRatio(mapper, out var ratio)
                        ? ratio
                        : (4.0 / 3.0) / Math.Max(1e-6, overlay.Width / Math.Max(1.0, overlay.Height));

                    if(_valid && AlmostEq(_overlayRect, overlay) && Math.Abs(_screenRatio - sr) < 1e-6)
                        return true;

                    if(!TryMakeRegionDrawer(mapper, out var drawer, overlay, sr, out var drawBoard, out var drawHand, out var drawHero))
                        return false;

                    var fb = new Rect[7]; var ob = new Rect[7]; var fh = new Rect[10];
                    for(int pos = 1; pos <= 7; pos++)
                    {
                        var rF = drawBoard(7, pos, true);
                        var rO = drawBoard(7, pos, false);
                        fb[pos - 1] = rF;
                        ob[pos - 1] = rO;
                    }
                    for(int pos = 1; pos <= 10; pos++)
                    {
                        var r = drawHand(10, pos, true);
                        fh[pos - 1] = r;
                    }

                    var hpF = drawHero != null ? drawHero(true) : Rect.Empty;

                    _friendlyBoard = fb; _opponentBoard = ob; _friendlyHand = fh; _friendlyHeroPower = hpF;
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

            public bool TryGetHeroPowerRect(bool friendly, out Rect rect)
            {
                rect = Rect.Empty;
                if(!_valid) return false;
                if(friendly)
                {
                    rect = _friendlyHeroPower;
                    return rect.Width > 0 && rect.Height > 0;
                }
                // Opponent hero power rarely used; return false to fall back to constants
                return false;
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

            internal static bool TryGetOverlayScreenRatio(IOverlayCoordinateMapper mapper, out double ratio)
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
                            var prop = overlayWindow.GetType().GetProperty("ScreenRatio", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
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

            internal static bool TryMakeRegionDrawer(IOverlayCoordinateMapper mapper, out object? drawer, Rect overlayRect, double screenRatio,
                out Func<int,int,bool,Rect> drawBoard, out Func<int,int,bool,Rect> drawHand, out Func<bool,Rect> drawHeroPower)
            {
                drawer = null; drawBoard = null; drawHand = null; drawHeroPower = null;
                try
                {
                    // Prefer resolving RegionDrawer from the same assembly that defines the mapper
                    var hostAsm = mapper.GetType().Assembly;
                    var rdType = hostAsm.GetType("Hearthstone_Deck_Tracker.Utility.RegionDrawer.RegionDrawer");
                    if(rdType == null)
                    {
                        // Fallback: search for the HDT app assembly by name
                        var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a == hostAsm || a.GetName().Name == "Hearthstone Deck Tracker");
                        if(asm != null)
                            rdType = asm.GetType("Hearthstone_Deck_Tracker.Utility.RegionDrawer.RegionDrawer");
                    }
                    if(rdType == null) return false;
                    var ctor = rdType.GetConstructor(new[] { typeof(double), typeof(double), typeof(double) });
                    if(ctor == null) return false;
                    var tmpDrawer = ctor.Invoke(new object?[] { overlayRect.Height, overlayRect.Width, screenRatio });

                    var drawBoardMi = rdType.GetMethod("DrawBoardCardRegions", BindingFlags.Public | BindingFlags.Instance);
                    var drawHandMi = rdType.GetMethod("DrawHandCardRegions", BindingFlags.Public | BindingFlags.Instance);
                    var drawHeroMi = rdType.GetMethod("DrawHeroPowerRegion", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(bool) }, null);
                    if(drawBoardMi == null || drawHandMi == null) return false;

                    var drawerObj = tmpDrawer; // capture local, not out param
                    drawBoard = (slots, pos, friendly) => TakePrimaryAndMap(drawBoardMi, drawerObj, overlayRect, new object?[] { slots, pos, friendly, 0, 0 });
                    drawHand = (slots, pos, friendly) => TakePrimaryAndMap(drawHandMi, drawerObj, overlayRect, new object?[] { slots, pos, friendly, null, 0, 0 });
                    if(drawHeroMi != null)
                        drawHeroPower = (friendly) => ToScreenRect((Rect)drawHeroMi.Invoke(drawerObj, new object?[] { friendly }), overlayRect);
                    drawer = tmpDrawer;
                    return true;
                }
                catch { drawer = null; drawBoard = null; drawHand = null; drawHeroPower = null; return false; }
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
            private static MethodInfo? _isPlayersTurnStatic;

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

                    // Static helper to tell if it's the player's turn
                    var entityHelper = asm.GetType("Hearthstone_Deck_Tracker.Utility.BoardDamage.EntityHelper");
                    if(entityHelper != null)
                    {
                        _isPlayersTurnStatic = entityHelper.GetMethod("IsPlayersTurn", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new[] { dictType }, null);
                    }
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

            public static bool TryGetBoardHandMaxEntityId(out int maxEntityId)
            {
                maxEntityId = 0;
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

                    // Resolve helpers once
                    var isInPlayPi = _entityType.GetProperty("IsInPlay");
                    var isInHandPi = _entityType.GetProperty("IsInHand");
                    var idPi = _entityType.GetProperty("Id");
                    foreach(var e in values)
                    {
                        bool inPlay = (bool)(isInPlayPi?.GetValue(e, null) ?? false);
                        bool inHand = (bool)(isInHandPi?.GetValue(e, null) ?? false);
                        if(!inPlay && !inHand)
                            continue;
                        var id = idPi?.GetValue(e, null);
                        if(id is int i && i > maxEntityId)
                            maxEntityId = i;
                    }
                    return true;
                }
                catch { return false; }
            }

            public static bool TryIsPlayersTurn(out bool isPlayersTurn)
            {
                isPlayersTurn = false;
                try
                {
                    EnsureInit();
                    if(_coreGameProp == null || _entitiesProp == null || _isPlayersTurnStatic == null)
                        return false;
                    var game = _coreGameProp.GetValue(null, null);
                    var entities = _entitiesProp.GetValue(game);
                    if(entities == null)
                        return false;
                    var res = _isPlayersTurnStatic.Invoke(null, new object?[] { entities });
                    if(res is bool b)
                    {
                        isPlayersTurn = b; return true;
                    }
                    return false;
                }
                catch { return false; }
            }
        }
    }
}
