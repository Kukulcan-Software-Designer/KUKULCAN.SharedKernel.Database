using KUKULCAN.SharedKernel.Abstractions.Capabilities;
using KUKULCAN.SharedKernel.Domain;

namespace KUKULCAN.SharedKernel.Database.Client.Client;

/// <summary>Demonstrates auditing and soft deletion.</summary>
public sealed class ClientProduct : AuditableEntity<ClientEntityId>, ISoftDelete
{
    private ClientProduct()
    {
    }

    public string Name { get; private set; } = string.Empty;
    public decimal Price { get; private set; }
    public string Category { get; private set; } = string.Empty;
    public bool IsDeleted { get; private set; }
    public DateTimeOffset? DeletedOn { get; private set; }

    public static ClientProduct Create(string name, decimal price, string category)
        => new()
        {
            Id = new ClientEntityId(Guid.NewGuid()),
            Name = name,
            Price = price,
            Category = category
        };

    public void ChangePrice(decimal price) => Price = price;

    public void Restore()
    {
        IsDeleted = false;
        DeletedOn = null;
    }
}
