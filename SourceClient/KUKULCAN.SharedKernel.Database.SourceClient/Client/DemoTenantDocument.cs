namespace KUKULCAN.SharedKernel.Database.Client.Client;

/// <summary>Demonstrates tenant filtering through a persistence-level TenantId property.</summary>
public sealed class DemoTenantDocument
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;

    public static DemoTenantDocument Create(Guid tenantId, string title, string content)
        => new() { TenantId = tenantId, Title = title, Content = content };
}
