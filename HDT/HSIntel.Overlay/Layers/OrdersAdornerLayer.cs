using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace HSIntel.Overlay.Layers
{
    public sealed class OrdersAdornerLayer : IntelAdornerLayer
    {
        private readonly VisualHost _host = new VisualHost();

        private static readonly Typeface Typeface = new Typeface("Segoe UI");
        private static readonly Brush BadgeFill;
        private static readonly Pen BadgeStroke;
        private static readonly Brush TextBrush;

        static OrdersAdornerLayer()
        {
            var fill = new SolidColorBrush(Color.FromArgb(0xC0, 0x33, 0x99, 0xFF));
            var stroke = new SolidColorBrush(Color.FromArgb(0xE0, 0x11, 0x55, 0xAA));
            var text = new SolidColorBrush(Colors.White);
            TryFreeze(fill);
            TryFreeze(stroke);
            TryFreeze(text);
            BadgeFill = fill;
            BadgeStroke = new Pen(stroke, 1.5);
            TryFreeze(BadgeStroke);
            TextBrush = text;
        }

        public OrdersAdornerLayer()
        {
            Children.Add(_host);
            _host.IsHitTestVisible = false;
            Loaded += (_, __) => SyncHostSize();
            SizeChanged += (_, __) => SyncHostSize();
        }

        public void SetOrders(IEnumerable<object>? orders)
        {
            StoreSnapshot(orders);

            var items = orders?.OfType<OrderGlyph>().ToList() ?? new List<OrderGlyph>();
            var visuals = new List<DrawingVisual>(items.Count);
            foreach(var o in items)
            {
                var dv = new DrawingVisual();
                using(var dc = dv.RenderOpen())
                {
                    DrawBadge(dc, o.Position, o.Number);
                }
                visuals.Add(dv);
            }
            _host.ReplaceAll(visuals);
        }

        private static void DrawBadge(DrawingContext dc, Point center, int number)
        {
            const double radius = 14.0;
            var ellipse = new EllipseGeometry(center, radius, radius);
            TryFreeze(ellipse);
            dc.DrawGeometry(BadgeFill, BadgeStroke, ellipse);

            var text = new FormattedText(
                number.ToString(),
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                Typeface,
                12,
                TextBrush);

            var origin = new Point(center.X - text.Width / 2, center.Y - text.Height / 2);
            dc.DrawText(text, origin);
        }

        internal sealed class OrderGlyph
        {
            public OrderGlyph(Point position, int number)
            { Position = position; Number = number; }
            public Point Position { get; }
            public int Number { get; }
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
