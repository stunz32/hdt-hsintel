using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace HSIntel.Overlay.Layers
{
    public sealed class HaloAdornerLayer : IntelAdornerLayer
    {
        private readonly VisualHost _host = new VisualHost();

        private static readonly Brush FriendlyHalo;
        private static readonly Brush EnemyHalo;

        static HaloAdornerLayer()
        {
            var fr = new RadialGradientBrush(Color.FromArgb(0x88, 0x7C, 0xFC, 0x00), Color.FromArgb(0x18, 0x7C, 0xFC, 0x00));
            fr.RadiusX = fr.RadiusY = 0.9; fr.Center = fr.GradientOrigin = new Point(0.5, 0.5);
            var er = new RadialGradientBrush(Color.FromArgb(0x88, 0xFF, 0x33, 0x33), Color.FromArgb(0x18, 0xFF, 0x33, 0x33));
            er.RadiusX = er.RadiusY = 0.9; er.Center = er.GradientOrigin = new Point(0.5, 0.5);
            TryFreeze(fr); TryFreeze(er);
            FriendlyHalo = fr; EnemyHalo = er;
        }

        public HaloAdornerLayer()
        {
            Children.Add(_host);
            _host.IsHitTestVisible = false;
            Loaded += (_, __) => SyncHostSize();
            SizeChanged += (_, __) => SyncHostSize();
        }

        public void SetHalos(IEnumerable<object>? halos)
        {
            StoreSnapshot(halos);

            var items = halos?.OfType<HaloGlyph>().ToList() ?? new List<HaloGlyph>();
            var visuals = new List<DrawingVisual>(items.Count);
            foreach(var h in items)
            {
                var dv = new DrawingVisual();
                using(var dc = dv.RenderOpen())
                {
                    var brush = h.IsFriendly ? FriendlyHalo : EnemyHalo;
                    var geo = new EllipseGeometry(h.Center, h.Radius, h.Radius);
                    TryFreeze(geo);
                    dc.DrawGeometry(brush, null, geo);
                }
                visuals.Add(dv);
            }
            _host.ReplaceAll(visuals);
        }

        internal sealed class HaloGlyph
        {
            public HaloGlyph(Point center, double radius, bool isFriendly)
            { Center = center; Radius = radius; IsFriendly = isFriendly; }
            public Point Center { get; }
            public double Radius { get; }
            public bool IsFriendly { get; }
        }

        private sealed class VisualHost : FrameworkElement
        {
            private readonly VisualCollection _visuals;
            public VisualHost()
            {
                _visuals = new VisualCollection(this);
                IsHitTestVisible = false;
            }
            public void ReplaceAll(IEnumerable<DrawingVisual> visuals)
            {
                _visuals.Clear();
                foreach(var v in visuals)
                    _visuals.Add(v);
            }
            protected override int VisualChildrenCount => _visuals.Count;
            protected override Visual GetVisualChild(int index) => _visuals[index];
        }

        private static void TryFreeze(Freezable f)
        {
            try { if(f.CanFreeze) f.Freeze(); } catch { }
        }

        private void SyncHostSize()
        {
            _host.Width = double.IsNaN(ActualWidth) || ActualWidth <= 0 ? double.NaN : ActualWidth;
            _host.Height = double.IsNaN(ActualHeight) || ActualHeight <= 0 ? double.NaN : ActualHeight;
        }
    }
}
