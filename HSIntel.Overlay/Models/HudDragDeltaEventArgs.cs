using System;
using System.Windows;

namespace HSIntel.Overlay.Models
{
    public sealed class HudDragDeltaEventArgs : EventArgs
    {
        public HudDragDeltaEventArgs(Vector delta)
        {
            Delta = delta;
        }

        public Vector Delta { get; }
    }
}
