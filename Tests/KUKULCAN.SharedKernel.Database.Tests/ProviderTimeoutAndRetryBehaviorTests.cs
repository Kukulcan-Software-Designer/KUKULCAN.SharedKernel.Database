using KUKULCAN.SharedKernel.Database.Tests.TestInfrastructure.internals;

namespace KUKULCAN.SharedKernel.Database.Tests;

[TestFixture]
[NonParallelizable]
public sealed class ProviderTimeoutAndRetryBehaviorTests
{
    private static readonly object[][] ProviderCases =
    [
        [DatabaseProvider.SqlServer, "Server=localhost;Database=KukulcanTests;Integrated Security=True;", "Microsoft.EntityFrameworkCore.SqlServer", "SqlServerRetryingExecutionStrategy"],
        [DatabaseProvider.PostgresSql, "Host=localhost;Database=KukulcanTests;Username=test;Password=test;", "Npgsql.EntityFrameworkCore.PostgreSQL", "NpgsqlRetryingExecutionStrategy"],
        [DatabaseProvider.MySql, "Server=localhost;Database=KukulcanTests;User Id=test;Password=test;", "MySql.EntityFrameworkCore", "MySQLRetryingExecutionStrategy"]
    ];

    [TestCaseSource(nameof(ProviderCases))]
    public void ConfigureProvider_ShouldExposeConfiguredCommandTimeout(
        DatabaseProvider provider,
        string connectionString,
        string expectedProviderName,
        string expectedRetryStrategyName)
    {
        using var context = CreateContext(provider, connectionString, timeoutSeconds: 47, retryEnabled: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(context.Database.ProviderName, Is.EqualTo(expectedProviderName));
            Assert.That(context.Database.GetCommandTimeout(), Is.EqualTo(47));
            Assert.That(context.Database.CreateExecutionStrategy().GetType().Name, Is.EqualTo(expectedRetryStrategyName));
        }
    }

    [TestCaseSource(nameof(ProviderCases))]
    public async Task ExecutionStrategy_ShouldRetryTransientTimeoutAndSucceed(
        DatabaseProvider provider,
        string connectionString,
        string expectedProviderName,
        string expectedRetryStrategyName)
    {
        await using var context = CreateContext(provider, connectionString, timeoutSeconds: 30, retryEnabled: true);
        var strategy = context.Database.CreateExecutionStrategy();
        var attempts = 0;

        int result = await strategy.ExecuteAsync(async () =>
        {
            attempts++;

            if (attempts == 1)
            {
                await Task.Yield();
                throw new TimeoutException("Synthetic transient timeout used to verify the provider execution strategy retry contract.");
            }

            return 42;
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(context.Database.ProviderName, Is.EqualTo(expectedProviderName));
            Assert.That(strategy.GetType().Name, Is.EqualTo(expectedRetryStrategyName));
            Assert.That(attempts, Is.EqualTo(2));
            Assert.That(result, Is.EqualTo(42));
        }
    }

    private static ProviderBehaviorTestDbContext CreateContext(
        DatabaseProvider provider,
        string connectionString,
        int timeoutSeconds,
        bool retryEnabled)
        => new(
            Options.Create(new KukulcanDatabaseOptions
            {
                Provider = provider,
                ConnectionString = connectionString,
                CommandTimeoutSeconds = timeoutSeconds,
                Retry = new KukulcanDatabaseOptions.RetryOptions
                {
                    Enabled = retryEnabled,
                    MaxRetryCount = 1,
                    MaxRetryDelaySeconds = 1
                }
            }),
            new TestTenantContext(Guid.NewGuid()),
            new TestClock(DateTimeOffset.UtcNow),
            Mock.Of<IDomainEventDispatcher>());

    private sealed class ProviderBehaviorTestDbContext(
        IOptions<KukulcanDatabaseOptions> options,
        ITenantContext tenantContext,
        IClock clock,
        IDomainEventDispatcher dispatcher)
        : KukulcanDbContextBase(options, tenantContext, clock, dispatcher);
}
