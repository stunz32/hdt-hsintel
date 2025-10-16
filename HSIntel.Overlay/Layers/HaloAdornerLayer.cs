using System.Collections.Generic;

namespace HSIntel.Overlay.Layers
{
    public sealed class HaloAdornerLayer : IntelAdornerLayer
    {
        public void SetHalos(IEnumerable<object>? halos)
        {
            StoreSnapshot(halos);
        }
    }
}
