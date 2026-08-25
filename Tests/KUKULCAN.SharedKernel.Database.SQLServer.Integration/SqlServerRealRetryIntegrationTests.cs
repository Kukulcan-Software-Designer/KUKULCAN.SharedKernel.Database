using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Storage;

namespace KUKULCAN.SharedKernel.Database.SQLServer.Integration;

[TestFixture]
[NonParallelizable]
public sealed class SqlServerRealRetryIntegrationTests
{
    [Test]
    public async Task ExecutionStrategy_ShouldRetryAfterRealSqlServerDeadlock()
    {
        Guid tenantId = Guid.NewGuid();
        var options = Options.Create(new KukulcanDatabaseOptions
        {
            Provider = DatabaseProvider.SqlServer,
            ConnectionString = SqlServerIntegrationDatabase.ConnectionString,
            CommandTimeoutSeconds = 30,
            Retry = new KukulcanDatabaseOptions.RetryOptions
            {
                Enabled = true,
                MaxRetryCount = 3,
                MaxRetryDelaySeconds = 1
            },
            Pool = new KukulcanDatabaseOptions.PoolOptions { Enabled = false }
        });

        await using var context = new SqlServerIntegrationDbContext(
            options,
            new SqlServerTenantContext(tenantId),
            new FixedClock(SqlServerIntegrationConstants.FixedNow),
            Mock.Of<IDomainEventDispatcher>());

        await context.Database.EnsureCreatedAsync();

        string connectionString = SqlServerIntegrationDatabase.ConnectionString;
        await using (SqlConnection setupConnection = new(connectionString))
        {
            await setupConnection.OpenAsync();
            await using SqlCommand setupCommand = setupConnection.CreateCommand();
            setupCommand.CommandText = """
                IF OBJECT_ID(N'dbo.KukulcanRetryCoverageRows', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.KukulcanRetryCoverageRows
                    (
                        Id int NOT NULL PRIMARY KEY,
                        Name nvarchar(100) NOT NULL
                    );
                END;
                DELETE FROM dbo.KukulcanRetryCoverageRows;
                INSERT INTO dbo.KukulcanRetryCoverageRows (Id, Name) VALUES (1, N'First'), (2, N'Second');
                """;
            await setupCommand.ExecuteNonQueryAsync();
        }

        await using SqlConnection blockerConnection = new(connectionString);
        await blockerConnection.OpenAsync();
        await using SqlTransaction blockerTransaction = (SqlTransaction)await blockerConnection.BeginTransactionAsync();

        await using SqlConnection victimConnection = new(connectionString);
        await victimConnection.OpenAsync();
        await using SqlTransaction victimTransaction = (SqlTransaction)await victimConnection.BeginTransactionAsync();

        await SetDeadlockPriorityAsync(victimConnection, victimTransaction, "LOW");

        await using (SqlCommand blockerFirstLock = blockerConnection.CreateCommand())
        {
            blockerFirstLock.Transaction = blockerTransaction;
            blockerFirstLock.CommandText = "UPDATE dbo.KukulcanRetryCoverageRows SET Name = Name WHERE Id = 2;";
            await blockerFirstLock.ExecuteNonQueryAsync();
        }

        await using (SqlCommand victimFirstLock = victimConnection.CreateCommand())
        {
            victimFirstLock.Transaction = victimTransaction;
            victimFirstLock.CommandText = "UPDATE dbo.KukulcanRetryCoverageRows SET Name = Name WHERE Id = 1;";
            await victimFirstLock.ExecuteNonQueryAsync();
        }

        int attempts = 0;
        IExecutionStrategy executionStrategy = context.Database.CreateExecutionStrategy();

        Task blockerSecondLock = Task.Run(async () =>
        {
            await using SqlCommand command = blockerConnection.CreateCommand();
            command.Transaction = blockerTransaction;
            command.CommandText = "UPDATE dbo.KukulcanRetryCoverageRows SET Name = Name WHERE Id = 1;";
            try
            {
                await command.ExecuteNonQueryAsync();
                await blockerTransaction.CommitAsync();
            }
            catch
            {
                try
                {
                    await blockerTransaction.RollbackAsync();
                }
                catch
                {
                    // Best effort cleanup after SQL Server has selected a deadlock victim.
                }
            }
        });

        await Task.Delay(250);

        int result = await executionStrategy.ExecuteAsync(async () =>
        {
            int currentAttempt = Interlocked.Increment(ref attempts);

            if (currentAttempt == 1)
            {
                await using SqlCommand secondLockCommand = victimConnection.CreateCommand();
                secondLockCommand.Transaction = victimTransaction;
                secondLockCommand.CommandText = "UPDATE dbo.KukulcanRetryCoverageRows SET Name = Name WHERE Id = 2;";
                await secondLockCommand.ExecuteNonQueryAsync();

                return 0;
            }

            await using SqlConnection verificationConnection = new(connectionString);
            await verificationConnection.OpenAsync();
            await using SqlCommand verificationCommand = verificationConnection.CreateCommand();
            verificationCommand.CommandText = "SELECT COUNT(*) FROM dbo.KukulcanRetryCoverageRows WHERE Id IN (1, 2);";
            return Convert.ToInt32(await verificationCommand.ExecuteScalarAsync());
        });

        await blockerSecondLock;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(attempts, Is.EqualTo(2));
            Assert.That(result, Is.EqualTo(2));
        }
    }

    private static async Task SetDeadlockPriorityAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string priority)
    {
        await using SqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SET DEADLOCK_PRIORITY {priority};";
        await command.ExecuteNonQueryAsync();
    }
}
