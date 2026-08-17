namespace KUKULCAN.SharedKernel.Database.Tests.TestInfrastructure.internals;

internal sealed class TestDbContextWithOptions(IOptions<KukulcanDatabaseOptions> options, ITenantContext tenantContext,
    IClock clock, IDomainEventDispatcher dispatcher) : KukulcanDbContextBase(options, tenantContext, clock, dispatcher)
{
    protected override void ConfigureProvider(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseInMemoryDatabase(Guid.NewGuid().ToString());
}
