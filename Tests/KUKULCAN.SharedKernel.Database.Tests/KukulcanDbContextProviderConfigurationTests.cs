using System.Reflection;
using KUKULCAN.SharedKernel.Database.Tests.TestInfrastructure.internals;

namespace KUKULCAN.SharedKernel.Database.Tests;

[TestFixture]
public sealed class KukulcanDbContextProviderConfigurationTests
{
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
    public void ConfigureProvider_WithMySql_ShouldConfigureMySql()
    {
        using var context = new ProviderTestDbContext(
            Options.Create(new KukulcanDatabaseOptions
            {
                Provider = DatabaseProvider.MySql,
                ConnectionString = "Server=localhost;Database=KukulcanTests;User Id=test;Password=test;",
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
            Assert.That(context.Database.ProviderName, Is.EqualTo("MySql.EntityFrameworkCore"));
            Assert.That(context.Database.GetCommandTimeout(), Is.EqualTo(45));
        }
    }

    [Test]
    public void ConfigureProvider_WithMySql_WhenProviderThrows_ShouldWrapFailure()
    {
        MethodInfo method = typeof(KukulcanDbContextBase).GetMethod(
            "ConfigureMySql", BindingFlags.Static | BindingFlags.NonPublic)!;

        TargetInvocationException exception = Assert.Throws<TargetInvocationException>(() =>
            method.Invoke(null,
                [null, "ignored", 30, 0, TimeSpan.FromSeconds(5)]))!;

        Assert.That(exception.InnerException, Is.TypeOf<NotSupportedException>());
        Assert.That(exception.InnerException!.Message, Does.Contain("Failed to configure provider"));
    }

    [Test]
    public void ConfigureProvider_WithSqlServer_WhenProviderThrows_ShouldWrapFailure()
    {
        MethodInfo method = typeof(KukulcanDbContextBase).GetMethod(
            "ConfigureSqlServer", BindingFlags.Static | BindingFlags.NonPublic)!;

        TargetInvocationException exception = Assert.Throws<TargetInvocationException>(() =>
            method.Invoke(null,
                [null, "ignored", 30, 0, TimeSpan.FromSeconds(5)]))!;

        Assert.That(exception.InnerException, Is.TypeOf<NotSupportedException>());
        Assert.That(exception.InnerException!.Message, Does.Contain("Failed to configure provider"));
        Assert.That(exception.InnerException!.InnerException, Is.Not.Null);
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
