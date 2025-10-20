using System;
using System.Windows;
using System.Windows.Controls;
using Hearthstone_Deck_Tracker.HSIntel;
using Hearthstone_Deck_Tracker.Utility.Extensions;
using Hearthstone_Deck_Tracker.Utility.Logging;
using HSIntel.Core.Config;
using HSIntel.Overlay.Controls;
using HSIntel.Overlay.Models;
using Hearthstone_Deck_Tracker.API;

namespace Hearthstone_Deck_Tracker.Windows
{
    public partial class OverlayWindow
    {
        private IntelOverlayHost? _intelOverlayHost;
        private bool _intelOverlayMountHooked;
        private bool _intelArrowHooked;

        private void InitializeIntelCoachOverlay()
        {
            try
            {
                if(IntelOverlayMount == null)
                    return;

                if(!_intelOverlayMountHooked)
                {
                    IntelOverlayMount.SizeChanged += (_, _) => SyncIntelOverlayHostLayout();
                    _intelOverlayMountHooked = true;
                }

                if(_intelOverlayHost == null)
                {
                    _intelOverlayHost = new IntelOverlayHost();
                    _intelOverlayHost.HudPositionChanged += OnIntelHudPositionChanged;
                    _intelOverlayHost.HudEnabledChanged += OnIntelHudEnabledChanged;
                    _intelOverlayHost.HudDragStarted += OnIntelHudDragStarted;
                    _intelOverlayHost.HudDragCompleted += OnIntelHudDragCompleted;
                    // Debug arrow disabled for Phase 8 validation

                    IntelOverlayMount.Children.Clear();
                    IntelOverlayMount.Children.Add(_intelOverlayHost);
                    Log.Info($"[HSIntel][Overlay] Host attached. mountZ={Panel.GetZIndex(IntelOverlayMount)} hostZ={Panel.GetZIndex(_intelOverlayHost)} size={IntelOverlayMount.ActualWidth:0}x{IntelOverlayMount.ActualHeight:0}");
                }

                SyncIntelOverlayHostLayout();

                // Ensure debug overlay is ON for diagnostics and persist it.
                UpdateHsIntelConfig(cfg => cfg.DebugOverlayMode = true);
                var config = CloneConfig(Config.Instance.HSIntel ?? new HSIntelConfig());
                var coordinateMapper = new HSIntelOverlayCoordinateMapper(this);

                _intelOverlayHost.ApplyConfiguration(config);
                _intelOverlayHost.SetCoordinateMapper(coordinateMapper);

                // Keep HUD interactive when hovered; HDT overlay will pass clicks through when not over this element.
                SetIntelHudHitTest(true);
                // Debug arrow disabled
            }
            catch(Exception ex)
            {
                Log.Error($"[HSIntel][Overlay] Failed to initialize overlay host: {ex}");
            }
        }

        private void SyncIntelOverlayHostLayout()
        {
            if(IntelOverlayMount == null || _intelOverlayHost == null)
                return;

            var width = IntelOverlayMount.ActualWidth > 0 ? IntelOverlayMount.ActualWidth : CanvasInfo.ActualWidth;
            var height = IntelOverlayMount.ActualHeight > 0 ? IntelOverlayMount.ActualHeight : CanvasInfo.ActualHeight;

            if(double.IsNaN(width) || double.IsInfinity(width) || width <= 0)
                width = CanvasInfo.ActualWidth;
            if(double.IsNaN(height) || double.IsInfinity(height) || height <= 0)
                height = CanvasInfo.ActualHeight;

            _intelOverlayHost.Width = width;
            _intelOverlayHost.Height = height;

            Canvas.SetLeft(_intelOverlayHost, 0);
            Canvas.SetTop(_intelOverlayHost, 0);
        }

        private void OnIntelHudPositionChanged(object? sender, HudPositionChangedEventArgs e)
        {
            UpdateHsIntelConfig(cfg =>
            {
                cfg.HudLeftPct = e.LeftPercent;
                cfg.HudTopPct = e.TopPercent;
            });
        }

        private void OnIntelHudEnabledChanged(object? sender, bool isEnabled)
        {
            UpdateHsIntelConfig(cfg => cfg.CoachEnabled = isEnabled);
            if(_intelOverlayHost != null)
            {
                var snapshot = CloneConfig(Config.Instance.HSIntel ?? new HSIntelConfig());
                _intelOverlayHost.ApplyConfiguration(snapshot);
                SetIntelHudHitTest(true);
                // Debug arrow disabled
            }
        }

        private void SetIntelHudHitTest(bool enabled)
        {
            if(_intelOverlayHost == null)
                return;

            OverlayExtensions.SetIsOverlayHitTestVisible(_intelOverlayHost.Hud, enabled);
            OverlayExtensions.SetIsOverlayHitTestVisible(_intelOverlayHost.Hud.DragHandle, enabled);
            OverlayExtensions.SetIsOverlayHoverVisible(_intelOverlayHost.Hud, enabled);
            OverlayExtensions.SetIsOverlayHoverVisible(_intelOverlayHost.Hud.DragHandle, enabled);
        }

        private void OnIntelHudDragStarted(object? sender, EventArgs e) { }

        private void OnIntelHudDragCompleted(object? sender, EventArgs e) { }

        // Debug arrow removed


        private static HSIntelConfig CloneConfig(HSIntelConfig config) => new HSIntelConfig
        {
            BeamWidth = config.BeamWidth,
            Depth = config.Depth,
            MaxComputeMs = config.MaxComputeMs,
            EnableRollouts = config.EnableRollouts,
            RolloutSamples = config.RolloutSamples,
            HudLeftPct = config.HudLeftPct,
            HudTopPct = config.HudTopPct,
            CoachEnabled = config.CoachEnabled,
            DebugOverlayMode = config.DebugOverlayMode,
            PreferHdtData = config.PreferHdtData,
            EnableHearthstoneJsonFallback = config.EnableHearthstoneJsonFallback
        };

        private void UpdateHsIntelConfig(Action<HSIntelConfig> mutator)
        {
            var current = CloneConfig(Config.Instance.HSIntel ?? new HSIntelConfig());
            mutator(current);
            Config.Instance.HSIntel = current;
            Config.Save();
        }
    }
}
