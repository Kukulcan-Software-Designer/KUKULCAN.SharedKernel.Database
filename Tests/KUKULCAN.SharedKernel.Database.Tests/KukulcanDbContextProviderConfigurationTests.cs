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
                ConnectionString = "Server=localhost;Database=KukulcanTests;Integrated Security=True;",
                CommandTimeoutSeconds = 45,
                Retry = new KukulcanDatabaseOptions.RetryOptions
                {
                    Enabled = true,
                    MaxRetryCount = 7,
                    MaxRetryDelaySeconds = 12
                }
            }),
            new TestTenantContext(Guid.NewGuid()),
            new TestClock(DateTimeOffset.UtcNow),
            Mock.Of<IDomainEventDispatcher>());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(context.Database.ProviderName, Is.EqualTo("Microsoft.EntityFrameworkCore.SqlServer"));
            Assert.That(context.Database.GetCommandTimeout(), Is.EqualTo(45));
            Assert.That(context.Database.CreateExecutionStrategy().GetType().Name,
                Is.EqualTo("SqlServerRetryingExecutionStrategy"));
        }
    }

    [Test]
    public void ConfigureProvider_WithPostgresSql_ShouldConfigurePostgreSql()
    {
        using var context = new ProviderTestDbContext(
            Options.Create(new KukulcanDatabaseOptions
            {
                Provider = DatabaseProvider.PostgresSql,
                ConnectionString = "Host=localhost;Database=KukulcanTests;Username=test;Password=test;",
                CommandTimeoutSeconds = 45,
                Retry = new KukulcanDatabaseOptions.RetryOptions
                {
                    Enabled = true,
                    MaxRetryCount = 7,
                    MaxRetryDelaySeconds = 12
                }
            }),
            new TestTenantContext(Guid.NewGuid()),
            new TestClock(DateTimeOffset.UtcNow),
            Mock.Of<IDomainEventDispatcher>());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(context.Database.ProviderName, Is.EqualTo("Npgsql.EntityFrameworkCore.PostgreSQL"));
            Assert.That(context.Database.GetCommandTimeout(), Is.EqualTo(45));
            Assert.That(context.Database.CreateExecutionStrategy().GetType().Name,
                Is.EqualTo("NpgsqlRetryingExecutionStrategy"));
        }
    }

    [Test]
    public void ConfigureProvider_WhenRetryIsDisabled_ShouldNotEnableRetryingStrategy()
    {
        using var context = new ProviderTestDbContext(
            Options.Create(new KukulcanDatabaseOptions
            {
                Provider = DatabaseProvider.SqlServer,
                ConnectionString = "Server=localhost;Database=KukulcanTests;Integrated Security=True;",
                Retry = new KukulcanDatabaseOptions.RetryOptions { Enabled = false }
            }),
            new TestTenantContext(Guid.NewGuid()),
            new TestClock(DateTimeOffset.UtcNow),
            Mock.Of<IDomainEventDispatcher>());

        Assert.That(
            context.Database.CreateExecutionStrategy().GetType().Name,
            Is.EqualTo("SqlServerExecutionStrategy"));
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
