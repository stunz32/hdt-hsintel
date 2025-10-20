using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HSIntel.Core.Config;
using HSIntel.Overlay.Interfaces;
using HSIntel.Overlay.Layers;
using HSIntel.Overlay.Models;
using HSIntel.Overlay.Utils;

namespace HSIntel.Overlay.Controls
{
    public partial class IntelOverlayHost : UserControl
    {
        private double _hudLeftPct = 90.0;
        private double _hudTopPct = 90.0;
        private HSIntelConfig? _configSnapshot;
        private IOverlayCoordinateMapper? _coordinateMapper;
        private bool _debugMode;

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

            // Debug overlay mode
            SetDebugMode(_configSnapshot.DebugOverlayMode);
        }

        public void SetCoordinateMapper(IOverlayCoordinateMapper mapper)
        {
            _coordinateMapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            CoachHud.CoordinateMapper = _coordinateMapper;
            _coordinateMapper.Refresh();
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

            if(_debugMode)
                DebugLayer.RenderMarkers(new Size(ActualWidth, ActualHeight));
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
            CompositionTarget.Rendering -= OnCompositionTargetRendering;
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

        private void SetDebugMode(bool enabled)
        {
            _debugMode = enabled;
            OverlayLog.Enabled = enabled;
            DebugLayer.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;

            CompositionTarget.Rendering -= OnCompositionTargetRendering;
            if(enabled)
            {
                DebugLayer.RenderMarkers(new Size(ActualWidth, ActualHeight));
                CompositionTarget.Rendering += OnCompositionTargetRendering;
                OverlayLog.Info("DebugOverlayMode=ON");
            }
            else
            {
                OverlayLog.Info("DebugOverlayMode=OFF");
            }
        }

        private void OnCompositionTargetRendering(object? sender, EventArgs e)
        {
            if(!_debugMode)
                return;
            PollRecommendationDebug();
            LogRenderTick();
        }

        private void LogRenderTick()
        {
            try
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                var size = $"{ActualWidth:0}x{ActualHeight:0}";

                var badges = Orders.RetainedPayload.Count;
                var arrows = Arrows.RetainedPayload.Count;
                var halos = Halo.RetainedPayload.Count;

                // Naive mapped point estimate: badges=1, arrow endpoints=2, halo endpoints=2
                var mapped = badges + (arrows * 2) + (halos * 2);

                var visualsOrders = Orders.Children.Count;
                var visualsArrows = Arrows.Children.Count;
                var visualsHalos = Halo.Children.Count;
                var visualsDebug = DebugLayer.Children.Count;
                var visualsHud = 1; // CoachHud shell
                var totalVisuals = visualsOrders + visualsArrows + visualsHalos + visualsDebug + visualsHud;

                if(_coordinateMapper == null)
                    OverlayLog.Warn("CoordinateMapper=NULL (no mapping available)
");

                OverlayLog.Info(
                    $"RenderTick size={size} dpi={dpi.DpiScaleX:0.##}x{dpi.DpiScaleY:0.##} " +
                    $"payload{{badges={badges},arrows={arrows},halos={halos}}} " +
                    $"mapped_points={mapped} visuals{{orders={visualsOrders},arrows={visualsArrows},halos={visualsHalos},debug={visualsDebug},hud={visualsHud}}} total={totalVisuals}");
            }
            catch (Exception ex)
            {
                OverlayLog.Warn($"RenderTick error: {ex.Message}");
            }
        }

        private int _lastDebugStepCount = -1;
        private void PollRecommendationDebug()
        {
            try
            {
                var rsType = Type.GetType("HSIntel.Engine.Services.RecommendationService, HSIntel.Engine");
                var method = rsType?.GetMethod("GetCurrentActionsOrEmpty", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if(method == null)
                    return;

                var list = method.Invoke(null, null) as System.Collections.ICollection;
                var count = list?.Count ?? 0;
                if(count != _lastDebugStepCount)
                {
                    _lastDebugStepCount = count;
                    OverlayLog.Info($"RecommendationChanged steps={count}");
                }
            }
            catch { }
        }
    }
}
