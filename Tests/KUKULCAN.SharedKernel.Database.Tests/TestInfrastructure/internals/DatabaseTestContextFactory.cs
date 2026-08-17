namespace KUKULCAN.SharedKernel.Database.Tests.TestInfrastructure.internals;

internal static class DatabaseTestContextFactory
{
    public static (TestDbContext Context, TestClock Clock, TestTenantContext Tenant, Mock<IDomainEventDispatcher> Dispatcher)
        Create(Guid? tenantId = null, DateTimeOffset? now = null)
    {
        Guid tenant = tenantId ?? Guid.NewGuid();
        var clock = new TestClock(now ?? new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero));
        var tenantContext = new TestTenantContext(tenant);
        var dispatcher = new Mock<IDomainEventDispatcher>();

        dispatcher
            .Setup(x => x.DispatchAsync(It.IsAny<IDomainEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        IOptions<KukulcanDatabaseOptions> options = Options.Create(new KukulcanDatabaseOptions
        {
            Provider = DatabaseProvider.SqlServer,
            ConnectionString = "DataSource=tests"
        });

        var context = new TestDbContext(options, tenantContext, clock, dispatcher.Object);
        return (context, clock, tenantContext, dispatcher);
    }
}
