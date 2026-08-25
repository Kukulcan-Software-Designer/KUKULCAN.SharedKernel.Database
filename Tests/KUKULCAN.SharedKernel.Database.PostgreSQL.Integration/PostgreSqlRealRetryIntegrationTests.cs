using System.Data.Common;
using Microsoft.EntityFrameworkCore.Storage;
using NUnit.Framework;

namespace KUKULCAN.SharedKernel.Database.PostgreSQL.Integration;

[TestFixture]
[NonParallelizable]
public sealed class PostgreSqlRealRetryIntegrationTests
{
    [Test]
    public async Task ExecutionStrategy_ShouldRetryAfterRealPostgreSqlConnectionTermination()
    {
        Guid tenantId = Guid.NewGuid();
        var options = Options.Create(new KukulcanDatabaseOptions
        {
            Provider = DatabaseProvider.PostgresSql,
            ConnectionString = IntegrationTestDatabase.ConnectionString,
            CommandTimeoutSeconds = 30,
            Retry = new KukulcanDatabaseOptions.RetryOptions
            {
                Enabled = true,
                MaxRetryCount = 3,
                MaxRetryDelaySeconds = 1
            },
            Pool = new KukulcanDatabaseOptions.PoolOptions { Enabled = false }
        });

        await using var setupContext = new PostgreSqlDatabaseIntegrationTests.IntegrationDbContext(
            options,
            new PostgreSqlDatabaseIntegrationTests.IntegrationTenantContext(tenantId),
            new PostgreSqlDatabaseIntegrationTests.FixedClock(PostgreSqlDatabaseIntegrationTests.FixedNow),
            Mock.Of<IDomainEventDispatcher>());
        await setupContext.Database.EnsureCreatedAsync();

        int attempts = 0;
        IExecutionStrategy strategy = setupContext.Database.CreateExecutionStrategy();

        int result = await strategy.ExecuteAsync(async () =>
        {
            int attempt = Interlocked.Increment(ref attempts);

            await using var victimContext = new PostgreSqlDatabaseIntegrationTests.IntegrationDbContext(
                options,
                new PostgreSqlDatabaseIntegrationTests.IntegrationTenantContext(tenantId),
                new PostgreSqlDatabaseIntegrationTests.FixedClock(PostgreSqlDatabaseIntegrationTests.FixedNow),
                Mock.Of<IDomainEventDispatcher>());

            await victimContext.Database.OpenConnectionAsync();
            DbConnection victimConnection = victimContext.Database.GetDbConnection();

            if (attempt == 1)
            {
                await using DbCommand pidCommand = victimConnection.CreateCommand();
                pidCommand.CommandText = "SELECT pg_backend_pid();";
                int backendPid = Convert.ToInt32(await pidCommand.ExecuteScalarAsync());

                await using var killerContext = new PostgreSqlDatabaseIntegrationTests.IntegrationDbContext(
                    options,
                    new PostgreSqlDatabaseIntegrationTests.IntegrationTenantContext(tenantId),
                    new PostgreSqlDatabaseIntegrationTests.FixedClock(PostgreSqlDatabaseIntegrationTests.FixedNow),
                    Mock.Of<IDomainEventDispatcher>());
                await killerContext.Database.OpenConnectionAsync();

                await using DbCommand killCommand = killerContext.Database.GetDbConnection().CreateCommand();
                killCommand.CommandText = $"SELECT pg_terminate_backend({backendPid});";
                Assert.That(Convert.ToBoolean(await killCommand.ExecuteScalarAsync()), Is.True);

                await using DbCommand longRunningCommand = victimConnection.CreateCommand();
                longRunningCommand.CommandText = "SELECT pg_sleep(10);";
                await longRunningCommand.ExecuteScalarAsync();

                Assert.Fail("The terminated PostgreSQL connection unexpectedly completed its command.");
            }

            await using DbCommand verificationCommand = victimConnection.CreateCommand();
            verificationCommand.CommandText = "SELECT 42;";
            return Convert.ToInt32(await verificationCommand.ExecuteScalarAsync());
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(attempts, Is.EqualTo(2));
            Assert.That(result, Is.EqualTo(42));
        }
    }
}
