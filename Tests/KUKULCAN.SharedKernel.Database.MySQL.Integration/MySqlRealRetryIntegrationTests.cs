using System.Data.Common;
using Microsoft.EntityFrameworkCore.Storage;

namespace KUKULCAN.SharedKernel.Database.MySQL.Integration;

[TestFixture]
[NonParallelizable]
public sealed class MySqlRealRetryIntegrationTests
{
    [Test]
    public async Task ExecutionStrategy_ShouldRetryAfterRealMySqlLockWaitTimeout()
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

        await using var setupContext = new MySqlIntegrationDbContext(
            options,
            new MySqlTenantContext(tenantId),
            new FixedClock(MySqlIntegrationConstants.FixedNow),
            Mock.Of<IDomainEventDispatcher>());

        await setupContext.Database.EnsureCreatedAsync();

        await using (DbConnection setupConnection = setupContext.Database.GetDbConnection())
        {
            await setupConnection.OpenAsync();

            await using DbCommand setupCommand = setupConnection.CreateCommand();
            setupCommand.CommandText = """
                CREATE TABLE IF NOT EXISTS KukulcanRetryCoverageRows
                (
                    Id INT NOT NULL PRIMARY KEY,
                    Name VARCHAR(100) NOT NULL
                );
                DELETE FROM KukulcanRetryCoverageRows;
                INSERT INTO KukulcanRetryCoverageRows (Id, Name) VALUES (1, 'Locked row');
                """;
            await setupCommand.ExecuteNonQueryAsync();
        }

        await using var blockerContext = new MySqlIntegrationDbContext(
            options,
            new MySqlTenantContext(tenantId),
            new FixedClock(MySqlIntegrationConstants.FixedNow),
            Mock.Of<IDomainEventDispatcher>());

        await blockerContext.Database.OpenConnectionAsync();
        DbConnection blockerConnection = blockerContext.Database.GetDbConnection();
        await using DbTransaction blockerTransaction = await blockerConnection.BeginTransactionAsync();

        await using (DbCommand lockCommand = blockerConnection.CreateCommand())
        {
            lockCommand.Transaction = blockerTransaction;
            lockCommand.CommandText = "UPDATE KukulcanRetryCoverageRows SET Name = Name WHERE Id = 1;";
            await lockCommand.ExecuteNonQueryAsync();
        }

        Task releaseBlockerTask = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            await blockerTransaction.RollbackAsync();
        });

        int attempts = 0;
        await using var executionContext = new MySqlIntegrationDbContext(
            options,
            new MySqlTenantContext(tenantId),
            new FixedClock(MySqlIntegrationConstants.FixedNow),
            Mock.Of<IDomainEventDispatcher>());

        IExecutionStrategy strategy = executionContext.Database.CreateExecutionStrategy();

        int result = await strategy.ExecuteAsync(async () =>
        {
            Interlocked.Increment(ref attempts);

            await using var victimContext = new MySqlIntegrationDbContext(
                options,
                new MySqlTenantContext(tenantId),
                new FixedClock(MySqlIntegrationConstants.FixedNow),
                Mock.Of<IDomainEventDispatcher>());

            await victimContext.Database.OpenConnectionAsync();
            DbConnection victimConnection = victimContext.Database.GetDbConnection();

            await using (DbCommand timeoutCommand = victimConnection.CreateCommand())
            {
                timeoutCommand.CommandText = "SET SESSION innodb_lock_wait_timeout = 1;";
                await timeoutCommand.ExecuteNonQueryAsync();
            }

            await using DbCommand updateCommand = victimConnection.CreateCommand();
            updateCommand.CommandText = "UPDATE KukulcanRetryCoverageRows SET Name = CONCAT(Name, ' updated') WHERE Id = 1;";

            return Convert.ToInt32(await updateCommand.ExecuteNonQueryAsync());
        });

        await releaseBlockerTask;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(strategy.RetriesOnFailure, Is.True);
            Assert.That(attempts, Is.EqualTo(2));
            Assert.That(result, Is.EqualTo(1));
        }
    }
}
