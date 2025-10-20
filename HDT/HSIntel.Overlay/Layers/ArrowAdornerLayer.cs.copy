using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace HSIntel.Overlay.Layers
{
    public sealed class ArrowAdornerLayer : IntelAdornerLayer
    {
        public void SetArrows(IEnumerable<object>? arrows)
        {
            StoreSnapshot(arrows);
        }

        public void ShowDebugArrow(Point source, Point target)
        {
            Children.Clear();

            var line = new Line
            {
                X1 = source.X,
                Y1 = source.Y,
                X2 = target.X,
                Y2 = target.Y,
                Stroke = new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0x66, 0x00)),
                StrokeThickness = 3,
                SnapsToDevicePixels = true,
                IsHitTestVisible = false
            };

            // Simple arrow head
            var dir = target - source;
            if(dir.Length > 0.1)
            {
                dir.Normalize();
                var perp = new Vector(-dir.Y, dir.X);
                var headLen = 12.0; var headWidth = 6.0;
                var basePoint = target - dir * headLen;
                var p1 = basePoint + perp * headWidth;
                var p2 = basePoint - perp * headWidth;

                var head = new Polygon
                {
                    Points = new PointCollection { target, p1, p2 },
                    Fill = new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0x66, 0x00)),
                    IsHitTestVisible = false
                };
                Children.Add(head);
            }

            Children.Add(line);
        }
    }
}
