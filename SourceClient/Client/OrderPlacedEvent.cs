using KUKULCAN.SharedKernel.DomainEvents.Abstractions;

namespace KUKULCAN.SharedKernel.Database.Client.Client;

/// <summary>Demo domain event.</summary>
public sealed record OrderPlacedEvent(Guid OrderId, string OrderNumber, decimal TotalAmount) : IDomainEvent
{
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}
