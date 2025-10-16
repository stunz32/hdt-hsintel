using System.Collections.Generic;

namespace HSIntel.Overlay.Layers
{
    public sealed class OrdersAdornerLayer : IntelAdornerLayer
    {
        public void SetOrders(IEnumerable<object>? orders)
        {
            StoreSnapshot(orders);
        }
    }
}
