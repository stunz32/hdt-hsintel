using System;
using System.Diagnostics;

namespace HSIntel.Overlay.Utils
{
    internal static class OverlayLog
    {
        private static volatile bool _enabled;

        public static bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        public static void Info(string message)
        {
            if(!_enabled)
                return;
            Trace.WriteLine($"[HSIntel][Overlay] {message}");
        }

        public static void Warn(string message)
        {
            if(!_enabled)
                return;
            Trace.WriteLine($"[HSIntel][Overlay][WARN] {message}");
        }
    }
}

