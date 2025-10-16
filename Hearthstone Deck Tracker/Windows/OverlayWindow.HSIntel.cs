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
                    _intelOverlayHost.SizeChanged += (_, __) => TryUpdateDebugArrow();

                    IntelOverlayMount.Children.Clear();
                    IntelOverlayMount.Children.Add(_intelOverlayHost);
                }

                SyncIntelOverlayHostLayout();

                var config = CloneConfig(Config.Instance.HSIntel ?? new HSIntelConfig());
                var coordinateMapper = new HSIntelOverlayCoordinateMapper(this);

                _intelOverlayHost.ApplyConfiguration(config);
                _intelOverlayHost.SetCoordinateMapper(coordinateMapper);

                // Keep HUD interactive when hovered; HDT overlay will pass clicks through when not over this element.
                SetIntelHudHitTest(true);
                // Hook a simple debug arrow update once per run to validate layering.
                if(!_intelArrowHooked)
                {
                    _intelArrowHooked = true;
                    GameEvents.OnTurnStart.Add(_ => TryUpdateDebugArrow());
                }

                TryUpdateDebugArrow();
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
                // Hook a simple debug arrow update once per run to validate layering.
                if(!_intelArrowHooked)
                {
                    _intelArrowHooked = true;
                    GameEvents.OnTurnStart.Add(_ => TryUpdateDebugArrow());
                }

                TryUpdateDebugArrow();
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

        private void TryUpdateDebugArrow()
        {
            try
            {
                if(_intelOverlayHost == null)
                    return;

                if(!_intelOverlayHost.Dispatcher.CheckAccess())
                {
                    _intelOverlayHost.Dispatcher.InvokeAsync(TryUpdateDebugArrow);
                    return;
                }

                var mapper = _intelOverlayHost?.Hud?.CoordinateMapper;
                if(mapper == null)
                    return;

                // Centers in screen coords
                var board = mapper.BoardRegion;
                var hand = mapper.PlayerHandRegion;
                if(board.Width <= 0 || board.Height <= 0 || hand.Width <= 0 || hand.Height <= 0)
                    return;

                var overlay = mapper.OverlayBounds;
                var srcScreen = new System.Windows.Point(board.X + board.Width / 2, board.Y + board.Height / 2);
                var dstScreen = new System.Windows.Point(hand.X + hand.Width / 2, hand.Y + hand.Height / 2);

                // Translate to overlay-host local coords
                var src = new System.Windows.Point(srcScreen.X - overlay.X, srcScreen.Y - overlay.Y);
                var dst = new System.Windows.Point(dstScreen.X - overlay.X, dstScreen.Y - overlay.Y);

                _intelOverlayHost.Arrows.ShowDebugArrow(src, dst);
            }
            catch(Exception ex)
            {
                Log.Debug("[HSIntel][Overlay] Debug arrow update failed: " + ex.Message);
            }
        }


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
