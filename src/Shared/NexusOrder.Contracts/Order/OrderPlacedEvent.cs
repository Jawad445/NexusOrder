namespace NexusOrder.Contracts;

public record OrderItemDto(Guid ProductId, int Quantity, decimal UnitPrice);

public record OrderPlacedEvent(
    Guid OrderId,
    Guid CustomerId,
    decimal TotalAmount,
    IReadOnlyList<OrderItemDto> Items,
    DateTime CreatedAtUtc
);
