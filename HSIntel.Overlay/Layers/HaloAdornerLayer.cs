using System.Collections.Generic;

namespace HSIntel.Overlay.Layers
{
    public sealed class HaloAdornerLayer : IntelAdornerLayer
    {
        protected override string LayerName => "Halos";
        public void SetHalos(IEnumerable<object>? halos)
        {
            StoreSnapshot(halos);
        }
    }
}
