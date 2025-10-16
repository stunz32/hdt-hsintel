using System;
using System.Windows;
using System.Windows.Controls;
using HSIntel.Core.Config;
using HSIntel.Overlay.Interfaces;
using HSIntel.Overlay.Layers;
using HSIntel.Overlay.Models;

namespace HSIntel.Overlay.Controls
{
    public partial class IntelOverlayHost : UserControl
    {
        private double _hudLeftPct = 90.0;
        private double _hudTopPct = 90.0;
        private HSIntelConfig? _configSnapshot;
        private IOverlayCoordinateMapper? _coordinateMapper;

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
    }
}
