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
                    return;

                // Read current engine recommendation; avoid allocations if unchanged.
                var actions = RecommendationService.GetCurrentActionsOrEmpty();
                if(actions == null || actions.Count == 0)
                {
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
            // Include live board/hand counts to force redraw when slots change
            if(HdtReflect.TryGetCounts(out var fBoard, out var oBoard, out var fHand))
                parts.Append($"@C:{fBoard},{oBoard},{fHand}");
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
            if(board.Width <= 0 || board.Height <= 0 || hand.Width <= 0 || hand.Height <= 0)
            {
                Orders.ClearSnapshot();
                Arrows.ClearSnapshot();
                Halo.ClearSnapshot();
                return;
            }

            // Recompute exact slot rectangles if size/ratio changed
            _slotCache.TryEnsure(mapper);

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
