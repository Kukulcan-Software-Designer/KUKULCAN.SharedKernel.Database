using System.Data.Common;

namespace KUKULCAN.SharedKernel.Database.MySQL.Integration;

[TestFixture]
[NonParallelizable]
public sealed class MySqlRealRetryIntegrationTests
{
    [Test]
    public async Task ExecutionStrategy_ShouldRetryAfterRealMySqlConnectionTermination()
    {
        Guid tenantId = Guid.NewGuid();
        var options = Options.Create(new KukulcanDatabaseOptions
        {
            Provider = DatabaseProvider.MySql,
            ConnectionString = MySqlIntegrationDatabase.ConnectionString,
            CommandTimeoutSeconds = 30,
            Retry = new KukulcanDatabaseOptions.RetryOptions
            {
                Enabled = true,
                MaxRetryCount = 3,
                MaxRetryDelaySeconds = 1
            },
            Pool = new KukulcanDatabaseOptions.PoolOptions { Enabled = false }
        });

        await using var setupContext = await MySqlIntegrationContextFactory.CreateAsync(tenantId);
        await setupContext.Database.CloseConnectionAsync();
        await setupContext.DisposeAsync();

        int attempts = 0;
        await using var executionContext = new MySqlIntegrationDbContext(
            options,
            new MySqlTenantContext(tenantId),
            new FixedClock(MySqlIntegrationConstants.FixedNow),
            Mock.Of<IDomainEventDispatcher>());
        await executionContext.Database.EnsureCreatedAsync();

        IExecutionStrategy strategy = executionContext.Database.CreateExecutionStrategy();

        int result = await strategy.ExecuteAsync(async () =>
        {
            int attempt = Interlocked.Increment(ref attempts);

            await using var victimContext = new MySqlIntegrationDbContext(
                options,
                new MySqlTenantContext(tenantId),
                new FixedClock(MySqlIntegrationConstants.FixedNow),
                Mock.Of<IDomainEventDispatcher>());

            await victimContext.Database.OpenConnectionAsync();
            DbConnection victimConnection = victimContext.Database.GetDbConnection();

            if (attempt == 1)
            {
                await using DbCommand threadCommand = victimConnection.CreateCommand();
                threadCommand.CommandText = "SELECT CONNECTION_ID();";
                long threadId = Convert.ToInt64(await threadCommand.ExecuteScalarAsync());

                await using var killerContext = new MySqlIntegrationDbContext(
                    options,
                    new MySqlTenantContext(tenantId),
                    new FixedClock(MySqlIntegrationConstants.FixedNow),
                    Mock.Of<IDomainEventDispatcher>());
                await killerContext.Database.OpenConnectionAsync();

                await using DbCommand killCommand = killerContext.Database.GetDbConnection().CreateCommand();
                killCommand.CommandText = $"KILL CONNECTION {threadId};";
                await killCommand.ExecuteNonQueryAsync();

                await using DbCommand longRunningCommand = victimConnection.CreateCommand();
                longRunningCommand.CommandText = "SELECT SLEEP(10);";
                await longRunningCommand.ExecuteScalarAsync();

                Assert.Fail("The terminated MySQL connection unexpectedly completed its command.");
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
