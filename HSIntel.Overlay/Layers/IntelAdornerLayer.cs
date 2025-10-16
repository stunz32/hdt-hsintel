using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;

namespace HSIntel.Overlay.Layers
{
    public abstract class IntelAdornerLayer : Canvas
    {
        private readonly List<object> _retainedPayload = new();

        protected IntelAdornerLayer()
        {
            IsHitTestVisible = false;
            Background = null;
            SnapsToDevicePixels = true;
        }

        public IReadOnlyList<object> RetainedPayload => _retainedPayload;

        protected void StoreSnapshot(IEnumerable<object>? payload)
        {
            _retainedPayload.Clear();

            if(payload == null)
                return;

            _retainedPayload.AddRange(payload.Where(p => p != null));
        }

        public void ClearSnapshot()
        {
            _retainedPayload.Clear();
            Children.Clear();
        }
    }
}
