namespace NexusOrder.Ordering.Domain;

public enum OrderStatus
{
    Pending = 0,
    InventoryReserved = 1,
    PaymentCompleted = 2,
    Allocated = 3,
    Cancelled = 4
}