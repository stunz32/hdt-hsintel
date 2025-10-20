using System;

namespace HSIntel.Overlay.Models
{
    public sealed class HudPositionChangedEventArgs : EventArgs
    {
        public HudPositionChangedEventArgs(double leftPercent, double topPercent)
        {
            LeftPercent = leftPercent;
            TopPercent = topPercent;
        }

        public double LeftPercent { get; }

        public double TopPercent { get; }
    }
}
