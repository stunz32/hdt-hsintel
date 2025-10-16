using System.Windows;

namespace HSIntel.Overlay.Interfaces
{
    public interface IOverlayCoordinateMapper
    {
        Rect HearthstoneScreenBounds { get; }

        Rect OverlayBounds { get; }

        Rect BoardRegion { get; }

        Rect PlayerHandRegion { get; }

        Point ScreenToBoard(Point screenPoint);

        Point BoardToScreen(Point boardPoint);

        Point ScreenToPlayerHand(Point screenPoint);

        Point PlayerHandToScreen(Point handPoint);

        void Refresh();
    }
}
