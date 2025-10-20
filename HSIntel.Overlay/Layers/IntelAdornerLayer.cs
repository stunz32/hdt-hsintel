using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using HSIntel.Overlay.Utils;

namespace HSIntel.Overlay.Layers
{
    public abstract class IntelAdornerLayer : Canvas
    {
        private readonly List<object> _retainedPayload = new();
        private int _lastSnapshotCount = 0;

        protected IntelAdornerLayer()
        {
            IsHitTestVisible = false;
            Background = null;
            SnapsToDevicePixels = true;
        }

        public IReadOnlyList<object> RetainedPayload => _retainedPayload;

        protected abstract string LayerName { get; }

        protected void StoreSnapshot(IEnumerable<object>? payload)
        {
            var prev = _retainedPayload.Count;
            _retainedPayload.Clear();

            var incoming = payload?.Where(p => p != null).ToList() ?? new List<object>();
            _retainedPayload.AddRange(incoming);

            var now = _retainedPayload.Count;
            OverlayLog.Info($"[{LayerName}] snapshot count={now} (prev={prev}), children={Children.Count}");
            _lastSnapshotCount = now;
        }

        public void ClearSnapshot()
        {
            _retainedPayload.Clear();
            Children.Clear();
            OverlayLog.Info($"[{LayerName}] cleared");
        }
    }
}
