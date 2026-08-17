namespace KUKULCAN.SharedKernel.Database.Tests.TestInfrastructure.internals;

internal sealed class TestTenantContext(Guid tenantId) : ITenantContext
{
    public Guid TenantId { get; } = tenantId;
}
