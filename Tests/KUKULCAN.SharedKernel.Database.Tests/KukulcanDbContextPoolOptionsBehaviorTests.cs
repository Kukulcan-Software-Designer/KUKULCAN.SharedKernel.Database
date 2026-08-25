using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace KUKULCAN.SharedKernel.Database.Tests;

[TestFixture]
public sealed class KukulcanDbContextPoolOptionsBehaviorTests
{
    private static readonly object[][] Cases =
    [
        [DatabaseProvider.SqlServer, "Server=localhost;Database=KukulcanTests;", "Pooling=true", "Min Pool Size=7", "Max Pool Size=31"],
        [DatabaseProvider.PostgresSql, "Host=localhost;Database=KukulcanTests;Username=test;Password=test;", "Pooling=true", "Minimum Pool Size=7", "Maximum Pool Size=31"],
        [DatabaseProvider.MySql, "Server=localhost;Database=KukulcanTests;User Id=test;Password=test;", "Pooling=true", "MinimumPoolSize=7", "MaximumPoolSize=31"]
    ];

    [TestCaseSource(nameof(Cases))]
    public void EnabledPoolOptions_ShouldProduceProviderSpecificConnectionString(
        DatabaseProvider provider,
        string baseConnectionString,
        string pooling,
        string minPool,
        string maxPool)
    {
        using var context = CreateContext(provider, baseConnectionString, new KukulcanDatabaseOptions.PoolOptions
        {
            Enabled = true,
            MinSize = 7,
            MaxSize = 31
        });

        string connectionString = context.Database.GetDbConnection().ConnectionString;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(connectionString, Does.Contain(pooling));
            Assert.That(connectionString, Does.Contain(minPool));
            Assert.That(connectionString, Does.Contain(maxPool));
        }
    }

    [TestCaseSource(nameof(Cases))]
    public void DisabledPoolOptions_ShouldRemoveProviderSpecificPoolingKeys(
        DatabaseProvider provider,
        string baseConnectionString,
        string pooling,
        string minPool,
        string maxPool)
    {
        string seededConnectionString = $"{baseConnectionString}Pooling=true;{minPool};{maxPool};";

        using var context = CreateContext(provider, seededConnectionString, new KukulcanDatabaseOptions.PoolOptions
        {
            Enabled = false,
            MinSize = 7,
            MaxSize = 31
        });

        string connectionString = context.Database.GetDbConnection().ConnectionString;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(connectionString, Does.Not.Contain(pooling));
            Assert.That(connectionString, Does.Not.Contain(minPool));
            Assert.That(connectionString, Does.Not.Contain(maxPool));
        }
    }

    private static PoolTestDbContext CreateContext(
        DatabaseProvider provider,
        string connectionString,
        KukulcanDatabaseOptions.PoolOptions pool)
        => new(
            Options.Create(new KukulcanDatabaseOptions
            {
                Provider = provider,
                ConnectionString = connectionString,
                Retry = new KukulcanDatabaseOptions.RetryOptions { Enabled = false },
                Pool = pool
            }),
            new PoolTestTenantContext(Guid.NewGuid()),
            new PoolTestClock(DateTimeOffset.UtcNow),
            Mock.Of<IDomainEventDispatcher>());

    private sealed class PoolTestDbContext(
        IOptions<KukulcanDatabaseOptions> options,
        ITenantContext tenantContext,
        IClock clock,
        IDomainEventDispatcher dispatcher)
        : KukulcanDbContextBase(options, tenantContext, clock, dispatcher);

    private sealed class PoolTestTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId { get; } = tenantId;
    }

    private sealed class PoolTestClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
