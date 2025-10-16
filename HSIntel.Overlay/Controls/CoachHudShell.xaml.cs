using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HSIntel.Core.Config;
using HSIntel.Overlay.Interfaces;
using HSIntel.Overlay.Models;

namespace HSIntel.Overlay.Controls
{
    public partial class CoachHudShell : UserControl
    {
        private bool _isDragging;
        private Point _lastDragPoint;
        private bool _isApplyingConfig;

        public CoachHudShell()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;

            HeaderRegion.MouseLeftButtonDown += OnHeaderMouseLeftButtonDown;
            HeaderRegion.MouseLeftButtonUp += OnHeaderMouseLeftButtonUp;
            HeaderRegion.MouseMove += OnHeaderMouseMove;
            HeaderRegion.MouseLeave += OnHeaderMouseLeave;
            ToggleState.Checked += OnToggleStateChanged;
            ToggleState.Unchecked += OnToggleStateChanged;

            // Enable drag from anywhere in the HUD surface (not just header).
            this.MouseLeftButtonDown += OnHeaderMouseLeftButtonDown;
            this.MouseLeftButtonUp += OnHeaderMouseLeftButtonUp;
            this.MouseMove += OnHeaderMouseMove;
            this.MouseLeave += OnHeaderMouseLeave;
        }

        public event EventHandler<HudDragDeltaEventArgs>? DragRequested;
        public event EventHandler? DragStarted;
        public event EventHandler? DragCompleted;
        public event EventHandler<bool>? CoachEnabledChanged;

        public static readonly DependencyProperty IsCoachEnabledProperty = DependencyProperty.Register(
            nameof(IsCoachEnabled),
            typeof(bool),
            typeof(CoachHudShell),
            new PropertyMetadata(true, OnIsCoachEnabledChanged));

        public bool IsCoachEnabled
        {
            get => (bool)GetValue(IsCoachEnabledProperty);
            set => SetValue(IsCoachEnabledProperty, value);
        }

        public IOverlayCoordinateMapper? CoordinateMapper { get; set; }

        public HSIntelConfig? ConfigSnapshot { get; private set; }

        public void SetConfigSnapshot(HSIntelConfig config)
        {
            ConfigSnapshot = Clone(config ?? throw new ArgumentNullException(nameof(config)));
            _isApplyingConfig = true;
            IsCoachEnabled = ConfigSnapshot.CoachEnabled;
            UpdateStatusText();
            _isApplyingConfig = false;
        }

        private static void OnIsCoachEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if(d is CoachHudShell shell)
                shell.UpdateStatusText();
        }

        private void UpdateStatusText()
        {
            if(StatusText == null || ToggleState == null)
                return;

            var enabled = IsCoachEnabled;
            StatusText.Text = enabled ? "Ready" : "Disabled";
            ToggleState.Content = enabled ? "On" : "Off";
            ToggleState.IsChecked = enabled;
        }

        private void OnLoaded(object sender, RoutedEventArgs e) => UpdateStatusText();

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            HeaderRegion.MouseLeftButtonDown -= OnHeaderMouseLeftButtonDown;
            HeaderRegion.MouseLeftButtonUp -= OnHeaderMouseLeftButtonUp;
            HeaderRegion.MouseMove -= OnHeaderMouseMove;
            HeaderRegion.MouseLeave -= OnHeaderMouseLeave;
            ToggleState.Checked -= OnToggleStateChanged;
            ToggleState.Unchecked -= OnToggleStateChanged;
            CompleteDrag();
        }

        private void OnHeaderMouseLeftButtonDown(object? sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            _lastDragPoint = e.GetPosition(this);
            CaptureMouse();
            DragStarted?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }

        private void OnHeaderMouseMove(object? sender, MouseEventArgs e)
        {
            if(!_isDragging)
                return;

            var currentPoint = e.GetPosition(this);
            var delta = currentPoint - _lastDragPoint;
            _lastDragPoint = currentPoint;

            if(Math.Abs(delta.X) < double.Epsilon && Math.Abs(delta.Y) < double.Epsilon)
                return;

            DragRequested?.Invoke(this, new HudDragDeltaEventArgs(new Vector(delta.X, delta.Y)));
        }

        private void OnHeaderMouseLeftButtonUp(object? sender, MouseButtonEventArgs e)
        {
            if(!_isDragging)
                return;

            CompleteDrag();
            e.Handled = true;
        }

        private void OnHeaderMouseLeave(object? sender, MouseEventArgs e)
        {
            if(_isDragging && e.LeftButton != MouseButtonState.Pressed)
                CompleteDrag();
        }

        private void OnToggleStateChanged(object sender, RoutedEventArgs e)
        {
            if(_isApplyingConfig)
                return;

            var enabled = ToggleState.IsChecked ?? false;
            IsCoachEnabled = enabled;
            CoachEnabledChanged?.Invoke(this, enabled);
        }

        private void CompleteDrag()
        {
            if(!_isDragging)
                return;

            _isDragging = false;
            ReleaseMouseCapture();
            DragCompleted?.Invoke(this, EventArgs.Empty);
        }

        private static HSIntelConfig Clone(HSIntelConfig config) => new HSIntelConfig
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
            public FrameworkElement DragHandle => HeaderRegion;
    }
}




