using Microsoft.Data.SqlClient;

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

        await using (SqlCommand command = blockerConnection.CreateCommand())
        {
            command.Transaction = blockerTransaction;
            command.CommandText = "UPDATE dbo.KukulcanRetryCoverageRows SET Name = Name WHERE Id = 2;";
            await command.ExecuteNonQueryAsync();
        }

        var blockerAttempting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task blockerTask = Task.Run(async () =>
        {
            try
            {
                await using SqlCommand command = blockerConnection.CreateCommand();
                command.Transaction = blockerTransaction;
                command.CommandText = "UPDATE dbo.KukulcanRetryCoverageRows SET Name = Name WHERE Id = 1;";
                blockerAttempting.TrySetResult();
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
                    // The deadlock can terminate either transaction first; cleanup is best effort.
                }

                throw;
            }
        });

        int attempts = 0;
        IExecutionStrategy executionStrategy = context.Database.CreateExecutionStrategy();

        int result = await executionStrategy.ExecuteAsync(async () =>
        {
            int currentAttempt = Interlocked.Increment(ref attempts);

            if (currentAttempt == 1)
            {
                await using SqlConnection victimConnection = new(connectionString);
                await victimConnection.OpenAsync();
                await using SqlTransaction victimTransaction = (SqlTransaction)await victimConnection.BeginTransactionAsync();

                await using (SqlCommand priorityCommand = victimConnection.CreateCommand())
                {
                    priorityCommand.Transaction = victimTransaction;
                    priorityCommand.CommandText = "SET DEADLOCK_PRIORITY LOW;";
                    await priorityCommand.ExecuteNonQueryAsync();
                }

                await using (SqlCommand firstLockCommand = victimConnection.CreateCommand())
                {
                    firstLockCommand.Transaction = victimTransaction;
                    firstLockCommand.CommandText = "UPDATE dbo.KukulcanRetryCoverageRows SET Name = Name WHERE Id = 1;";
                    await firstLockCommand.ExecuteNonQueryAsync();
                }

                await blockerAttempting.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await Task.Delay(100);

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

        await blockerTask;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(attempts, Is.EqualTo(2));
            Assert.That(result, Is.EqualTo(2));
        }
    }
}
