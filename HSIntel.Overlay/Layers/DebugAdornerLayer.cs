using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace HSIntel.Overlay.Layers
{
    public sealed class DebugAdornerLayer : Canvas
    {
        public DebugAdornerLayer()
        {
            IsHitTestVisible = false;
            Background = null;
            SnapsToDevicePixels = true;
        }

        public void RenderMarkers(Size size)
        {
            Children.Clear();
            if(size.Width <= 0 || size.Height <= 0)
                return;

            var stroke = new SolidColorBrush(Color.FromArgb(0xC0, 0x2A, 0x9D, 0x8F)); // teal-ish
            var thinStroke = new SolidColorBrush(Color.FromArgb(0xA0, 0xFF, 0xFF, 0xFF));

            // Crosshair lines
            var cx = size.Width / 2.0;
            var cy = size.Height / 2.0;
            Children.Add(new Line { X1 = 0, Y1 = cy, X2 = size.Width, Y2 = cy, Stroke = thinStroke, StrokeThickness = 1, IsHitTestVisible = false });
            Children.Add(new Line { X1 = cx, Y1 = 0, X2 = cx, Y2 = size.Height, Stroke = thinStroke, StrokeThickness = 1, IsHitTestVisible = false });

            // Corner dots
            AddDot(0, 0, stroke);
            AddDot(size.Width - 1, 0, stroke);
            AddDot(0, size.Height - 1, stroke);
            AddDot(size.Width - 1, size.Height - 1, stroke);
            // Center dot
            AddDot(cx, cy, new SolidColorBrush(Color.FromArgb(0xC0, 0xE6, 0x39, 0x46))); // red-ish
        }

        private void AddDot(double x, double y, Brush fill)
        {
            var e = new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = fill,
                Stroke = Brushes.Black,
                StrokeThickness = 1,
                IsHitTestVisible = false
            };
            SetLeft(e, x - e.Width / 2);
            SetTop(e, y - e.Height / 2);
            Children.Add(e);
        }
    }
}

