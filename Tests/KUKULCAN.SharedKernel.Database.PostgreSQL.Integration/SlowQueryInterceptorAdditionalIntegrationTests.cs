using KUKULCAN.SharedKernel.Database.Interceptors;
using NUnit.Framework;

namespace KUKULCAN.SharedKernel.Database.PostgreSQL.Integration;

[TestFixture]
[NonParallelizable]
public sealed class SlowQueryInterceptorAdditionalIntegrationTests
{
    [Test]
    public async Task SlowQueryInterceptor_ShouldLogRealPostgreSqlReaderCommandAboveThreshold()
    {
        int previousThreshold = SlowQueryInterceptor.SlowQueryThresholdMs;
        SlowQueryInterceptor.SlowQueryThresholdMs = 0;

        try
        {
            Guid tenantId = Guid.NewGuid();
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
                new PostgreSqlDatabaseIntegrationTests.IntegrationTenantContext(tenantId),
                new PostgreSqlDatabaseIntegrationTests.FixedClock(PostgreSqlDatabaseIntegrationTests.FixedNow),
                Mock.Of<IDomainEventDispatcher>(),
                interceptor);

            await context.Database.EnsureCreatedAsync();
            context.Entities.Add(new PostgreSqlDatabaseIntegrationTests.IntegrationEntity
            {
                TenantId = tenantId,
                Name = "Reader interceptor"
            });
            await context.SaveChangesAsync();

            List<PostgreSqlDatabaseIntegrationTests.IntegrationEntity> entities =
                await context.Entities.Where(x => x.Name == "Reader interceptor").ToListAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entities, Has.Count.EqualTo(1));
                Assert.That(logger.WarningMessages, Has.Some.Contains("[SlowQuery]"));
                Assert.That(logger.WarningMessages, Has.Some.Contains("[SQL hidden"));
            }
        }
        finally
        {
            SlowQueryInterceptor.SlowQueryThresholdMs = previousThreshold;
        }
    }

    [Test]
    public async Task SlowQueryInterceptor_ShouldIncludeSqlWhenSensitiveDataLoggingIsEnabled()
    {
        int previousThreshold = SlowQueryInterceptor.SlowQueryThresholdMs;
        SlowQueryInterceptor.SlowQueryThresholdMs = 0;

        try
        {
            Guid tenantId = Guid.NewGuid();
            var logger = new PostgreSqlDatabaseIntegrationTests.CapturingLogger<SlowQueryInterceptor>();
            var options = Options.Create(new KukulcanDatabaseOptions
            {
                Provider = DatabaseProvider.PostgresSql,
                ConnectionString = IntegrationTestDatabase.ConnectionString,
                CommandTimeoutSeconds = 30,
                EnableSensitiveDataLogging = true,
                Retry = new KukulcanDatabaseOptions.RetryOptions { Enabled = false },
                Pool = new KukulcanDatabaseOptions.PoolOptions { Enabled = false }
            });
            var interceptor = new SlowQueryInterceptor(logger, options);

            await using var context = new PostgreSqlDatabaseIntegrationTests.IntegrationDbContext(
                options,
                new PostgreSqlDatabaseIntegrationTests.IntegrationTenantContext(tenantId),
                new PostgreSqlDatabaseIntegrationTests.FixedClock(PostgreSqlDatabaseIntegrationTests.FixedNow),
                Mock.Of<IDomainEventDispatcher>(),
                interceptor);

            await context.Database.EnsureCreatedAsync();
            context.Entities.Add(new PostgreSqlDatabaseIntegrationTests.IntegrationEntity
            {
                TenantId = tenantId,
                Name = "Sensitive reader interceptor"
            });
            await context.SaveChangesAsync();

            _ = await context.Entities
                .Where(x => x.Name == "Sensitive reader interceptor")
                .ToListAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(logger.WarningMessages, Has.Some.Contains("[SlowQuery]"));
                Assert.That(logger.WarningMessages, Has.None.Contains("[SQL hidden"));
                Assert.That(logger.WarningMessages, Has.Some.Contains("SELECT"));
            }
        }
        finally
        {
            SlowQueryInterceptor.SlowQueryThresholdMs = previousThreshold;
        }
    }

    [Test]
    public async Task SlowQueryInterceptor_ShouldNotLogReaderCommandAtOrBelowThreshold()
    {
        int previousThreshold = SlowQueryInterceptor.SlowQueryThresholdMs;
        SlowQueryInterceptor.SlowQueryThresholdMs = int.MaxValue;

        try
        {
            Guid tenantId = Guid.NewGuid();
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
                new PostgreSqlDatabaseIntegrationTests.IntegrationTenantContext(tenantId),
                new PostgreSqlDatabaseIntegrationTests.FixedClock(PostgreSqlDatabaseIntegrationTests.FixedNow),
                Mock.Of<IDomainEventDispatcher>(),
                interceptor);

            await context.Database.EnsureCreatedAsync();
            _ = await context.Entities.Where(x => x.TenantId == tenantId).ToListAsync();

            Assert.That(logger.WarningMessages, Has.None.Contains("[SlowQuery]"));
        }
        finally
        {
            SlowQueryInterceptor.SlowQueryThresholdMs = previousThreshold;
        }
    }
}
