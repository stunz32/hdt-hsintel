using System;
using System.Windows;
using HSIntel.Overlay.Interfaces;

namespace HSIntel.Overlay.Services
{
    public abstract class OverlayCoordinateMapperBase : IOverlayCoordinateMapper
    {
        public Rect HearthstoneScreenBounds { get; protected set; }

        public Rect OverlayBounds { get; protected set; }

        public Rect BoardRegion { get; protected set; }

        public Rect PlayerHandRegion { get; protected set; }

        public Point ScreenToBoard(Point screenPoint) =>
            Transform(screenPoint, HearthstoneScreenBounds, BoardRegion);

        public Point BoardToScreen(Point boardPoint) =>
            Transform(boardPoint, BoardRegion, HearthstoneScreenBounds);

        public Point ScreenToPlayerHand(Point screenPoint) =>
            Transform(screenPoint, HearthstoneScreenBounds, PlayerHandRegion);

        public Point PlayerHandToScreen(Point handPoint) =>
            Transform(handPoint, PlayerHandRegion, HearthstoneScreenBounds);

        public abstract void Refresh();

        protected static Point Transform(Point point, Rect sourceSpace, Rect targetSpace)
        {
            if(sourceSpace.Width <= 0 || sourceSpace.Height <= 0)
                return new Point(targetSpace.X, targetSpace.Y);

            var relativeX = (point.X - sourceSpace.X) / sourceSpace.Width;
            var relativeY = (point.Y - sourceSpace.Y) / sourceSpace.Height;

            relativeX = Clamp(relativeX, 0, 1);
            relativeY = Clamp(relativeY, 0, 1);

            var targetX = targetSpace.X + (targetSpace.Width * relativeX);
            var targetY = targetSpace.Y + (targetSpace.Height * relativeY);

            return new Point(targetX, targetY);
        }

        protected static double Clamp(double value, double min, double max) =>
            Math.Max(min, Math.Min(max, value));

        protected void SetBounds(Rect hearthstone, Rect overlay, Rect board, Rect hand)
        {
            HearthstoneScreenBounds = hearthstone;
            OverlayBounds = overlay;
            BoardRegion = board;
            PlayerHandRegion = hand;
        }
    }
}
