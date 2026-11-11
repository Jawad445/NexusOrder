namespace NexusOrder.Ordering.Domain;

public sealed class Order
{
    private readonly List<OrderItem> _items = [];

    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public OrderStatus Status { get; private set; }
    public decimal TotalAmount { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();

    private Order() { } // Required for EF Core

    public static Order Create(Guid customerId, IEnumerable<(Guid productId, int quantity, decimal unitPrice)> items)
    {
        var itemList = items.ToList();
        if (itemList.Count == 0)
        {
            throw new InvalidOperationException("An order must contain at least one item.");
        }

        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            Status = OrderStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        };

        foreach (var (productId, quantity, unitPrice) in itemList)
        {
            order._items.Add(OrderItem.Create(productId, quantity, unitPrice));
        }

        order.TotalAmount = order._items.Sum(i => i.Quantity * i.UnitPrice);
        return order;
    }

    public void MarkInventoryReserved()
    {
        if (Status != OrderStatus.Pending)
            throw new InvalidOperationException($"Cannot transition to Reserved from state '{Status}'.");
        Status = OrderStatus.InventoryReserved;
    }

    public void MarkCancelled()
    {
        if (Status == OrderStatus.Allocated)
            throw new InvalidOperationException("Cannot cancel an already completed and allocated order.");
        Status = OrderStatus.Cancelled;
    }
}