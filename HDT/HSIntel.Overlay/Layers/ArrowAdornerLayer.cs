using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace HSIntel.Overlay.Layers
{
    public sealed class ArrowAdornerLayer : IntelAdornerLayer
    {
        private readonly VisualHost _host = new VisualHost();

        private static readonly Pen ArrowPen;
        private static readonly Brush ArrowFill;

        static ArrowAdornerLayer()
        {
            var stroke = new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0x66, 0x00));
            var fill = new SolidColorBrush(Color.FromArgb(0xC0, 0xFF, 0x66, 0x00));
            TryFreeze(stroke);
            TryFreeze(fill);
            ArrowPen = new Pen(stroke, 3.0);
            TryFreeze(ArrowPen);
            ArrowFill = fill;
        }

        public ArrowAdornerLayer()
        {
            Children.Add(_host);
            _host.IsHitTestVisible = false;
            Loaded += (_, __) => SyncHostSize();
            SizeChanged += (_, __) => SyncHostSize();
        }

        public void SetArrows(IEnumerable<object>? arrows)
        {
            StoreSnapshot(arrows);

            var items = arrows?.OfType<ArrowGlyph>().ToList() ?? new List<ArrowGlyph>();
            var visuals = new List<DrawingVisual>(items.Count);

            foreach(var a in items)
            {
                var dv = new DrawingVisual();
                using(var dc = dv.RenderOpen())
                {
                    DrawArrow(dc, a.From, a.To);
                }
                visuals.Add(dv);
            }

            _host.ReplaceAll(visuals);
        }

        public void ShowDebugArrow(Point source, Point target)
        {
            var dv = new DrawingVisual();
            using(var dc = dv.RenderOpen())
                DrawArrow(dc, source, target);
            _host.ReplaceAll(new[] { dv });
        }

        private static void DrawArrow(DrawingContext dc, Point source, Point target)
        {
            dc.DrawLine(ArrowPen, source, target);

            var dir = target - source;
            if(dir.Length <= 0.1)
                return;

            dir.Normalize();
            var perp = new Vector(-dir.Y, dir.X);
            var headLen = 12.0; var headWidth = 6.0;
            var basePoint = target - dir * headLen;
            var p1 = basePoint + perp * headWidth;
            var p2 = basePoint - perp * headWidth;

            var geo = new StreamGeometry();
            using(var gctx = geo.Open())
            {
                gctx.BeginFigure(target, true, true);
                gctx.LineTo(p1, true, true);
                gctx.LineTo(p2, true, true);
            }
            TryFreeze(geo);
            dc.DrawGeometry(ArrowFill, null, geo);
        }

        internal sealed class ArrowGlyph
        {
            public ArrowGlyph(Point from, Point to)
            { From = from; To = to; }
            public Point From { get; }
            public Point To { get; }
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
