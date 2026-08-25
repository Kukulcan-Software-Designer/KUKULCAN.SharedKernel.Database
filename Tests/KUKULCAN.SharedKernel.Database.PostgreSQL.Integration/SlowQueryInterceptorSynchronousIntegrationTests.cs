using KUKULCAN.SharedKernel.Database.Interceptors;
using NUnit.Framework;

namespace KUKULCAN.SharedKernel.Database.PostgreSQL.Integration;

[TestFixture]
[NonParallelizable]
public sealed class SlowQueryInterceptorSynchronousIntegrationTests
{
    [Test]
    public async Task SlowQueryInterceptor_ShouldLogSynchronousReaderCommandAgainstRealPostgreSql()
    {
        int previousThreshold = SlowQueryInterceptor.SlowQueryThresholdMs;
        SlowQueryInterceptor.SlowQueryThresholdMs = -1;

        try
        {
            var logger = new PostgreSqlDatabaseIntegrationTests.CapturingLogger<SlowQueryInterceptor>();
            var options = Options.Create(new KukulcanDatabaseOptions
            {
                Provider = DatabaseProvider.PostgresSql,
                ConnectionString = IntegrationTestDatabase.ConnectionString,
                CommandTimeoutSeconds = 30,
                Retry = new KukulcanDatabaseOptions.RetryOptions { Enabled = false },
                Pool = new KukulcanDatabaseOptions.PoolOptions { Enabled = false }
            });

            var interceptor = new SlowQueryInterceptor(logger, options);
            await using var context = new PostgreSqlDatabaseIntegrationTests.IntegrationDbContext(
                options,
                new PostgreSqlDatabaseIntegrationTests.IntegrationTenantContext(Guid.NewGuid()),
                new PostgreSqlDatabaseIntegrationTests.FixedClock(PostgreSqlDatabaseIntegrationTests.FixedNow),
                Mock.Of<IDomainEventDispatcher>(),
                interceptor);

            await context.Database.EnsureCreatedAsync();

            _ = context.Entities.ToList();

            Assert.That(logger.WarningMessages, Has.Some.Contains("[SlowQuery]"));
        }
        finally
        {
            SlowQueryInterceptor.SlowQueryThresholdMs = previousThreshold;
        }
    }

    [Test]
    public async Task SlowQueryInterceptor_ShouldLogSynchronousNonQueryCommandAgainstRealPostgreSql()
    {
        int previousThreshold = SlowQueryInterceptor.SlowQueryThresholdMs;
        SlowQueryInterceptor.SlowQueryThresholdMs = -1;

        try
        {
            var logger = new PostgreSqlDatabaseIntegrationTests.CapturingLogger<SlowQueryInterceptor>();
            var options = Options.Create(new KukulcanDatabaseOptions
            {
                Provider = DatabaseProvider.PostgresSql,
                ConnectionString = IntegrationTestDatabase.ConnectionString,
                CommandTimeoutSeconds = 30,
                Retry = new KukulcanDatabaseOptions.RetryOptions { Enabled = false },
                Pool = new KukulcanDatabaseOptions.PoolOptions { Enabled = false }
            });

            var interceptor = new SlowQueryInterceptor(logger, options);
            await using var context = new PostgreSqlDatabaseIntegrationTests.IntegrationDbContext(
                options,
                new PostgreSqlDatabaseIntegrationTests.IntegrationTenantContext(Guid.NewGuid()),
                new PostgreSqlDatabaseIntegrationTests.FixedClock(PostgreSqlDatabaseIntegrationTests.FixedNow),
                Mock.Of<IDomainEventDispatcher>(),
                interceptor);

            await context.Database.EnsureCreatedAsync();

            context.Database.ExecuteSqlRaw("SELECT 1;");

            Assert.That(logger.WarningMessages, Has.Some.Contains("[SlowQuery]"));
        }
        finally
        {
            SlowQueryInterceptor.SlowQueryThresholdMs = previousThreshold;
        }
    }
}
