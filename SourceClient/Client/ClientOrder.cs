using KUKULCAN.SharedKernel.Domain;

namespace KUKULCAN.SharedKernel.Database.Client.Client;

/// <summary>Demonstrates SharedKernel aggregate root domain events.</summary>
public sealed class ClientOrder : AuditableEntity<ClientEntityId>
{
    private ClientOrder()
    {
    }

    public string OrderNumber { get; private set; } = string.Empty;
    public decimal TotalAmount { get; private set; }
    public string Status { get; private set; } = "Pending";

    public static ClientOrder Create(string orderNumber, decimal totalAmount, string status = "Pending")
        => new()
        {
            Id = new ClientEntityId(Guid.NewGuid()),
            OrderNumber = orderNumber,
            TotalAmount = totalAmount,
            Status = status
        };

    public void Place()
    {
        AddDomainEvent(new OrderPlacedEvent(Id.Value, OrderNumber, TotalAmount));
    }
}
