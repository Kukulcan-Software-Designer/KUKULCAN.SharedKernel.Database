using KUKULCAN.SharedKernel.Database.Abstractions;

namespace KUKULCAN.SharedKernel.Database.Client.Client;

public sealed class ConsoleTenantContext : ITenantContext
{
    public Guid TenantId { get; private set; } = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    public void SetTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("TenantId cannot be empty.", nameof(tenantId));
        TenantId = tenantId;
    }
}