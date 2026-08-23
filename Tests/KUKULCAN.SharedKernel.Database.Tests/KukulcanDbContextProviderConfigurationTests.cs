using KUKULCAN.SharedKernel.Database.Tests.TestInfrastructure.internals;

namespace KUKULCAN.SharedKernel.Database.Tests;

[TestFixture]
public sealed class KukulcanDbContextProviderConfigurationTests
{
    [Test]
    public void ConfigureProvider_WithSqlServer_ShouldConfigureSqlServer()
    {
        using var context = new ProviderTestDbContext(
            Options.Create(new KukulcanDatabaseOptions
            {
                Provider = DatabaseProvider.SqlServer,
                ConnectionString = "Server=localhost;Database=KukulcanTests;Integrated Security=True;"
            }),
            new TestTenantContext(Guid.NewGuid()),
            new TestClock(DateTimeOffset.UtcNow),
            Mock.Of<IDomainEventDispatcher>());

        Assert.That(
            context.Database.ProviderName,
            Is.EqualTo("Microsoft.EntityFrameworkCore.SqlServer"));
    }

    [Test]
    public void ConfigureProvider_WithPostgresSql_ShouldConfigurePostgreSql()
    {
        using var context = new ProviderTestDbContext(
            Options.Create(new KukulcanDatabaseOptions
            {
                Provider = DatabaseProvider.PostgresSql,
                ConnectionString = "Host=localhost;Database=KukulcanTests;Username=test;Password=test;"
            }),
            new TestTenantContext(Guid.NewGuid()),
            new TestClock(DateTimeOffset.UtcNow),
            Mock.Of<IDomainEventDispatcher>());

        Assert.That(
            context.Database.ProviderName,
            Is.EqualTo("Npgsql.EntityFrameworkCore.PostgreSQL"));
    }

    private sealed class ProviderTestDbContext(
        IOptions<KukulcanDatabaseOptions> options,
        ITenantContext tenantContext,
        IClock clock,
        IDomainEventDispatcher dispatcher)
        : KukulcanDbContextBase(options, tenantContext, clock, dispatcher)
    {
    }
}
