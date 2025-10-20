using System.Collections.Generic;

namespace HSIntel.Overlay.Layers
{
    public sealed class OrdersAdornerLayer : IntelAdornerLayer
    {
        protected override string LayerName => "Orders";
        public void SetOrders(IEnumerable<object>? orders)
        {
            StoreSnapshot(orders);
        }
    }
}
