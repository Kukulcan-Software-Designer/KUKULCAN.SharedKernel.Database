namespace KUKULCAN.SharedKernel.Database.Integration.SQLServer;

[TestFixture]
[NonParallelizable]
public sealed class SqlServerCancellationIntegrationTests
{
    [Test]
    public async Task SaveChanges_ShouldHonorCancellationTokenAgainstRealSqlServer()
    {
        await using var context = await SqlServerIntegrationContextFactory.CreateAsync(Guid.NewGuid());
        context.Entities.Add(new SqlServerIntegrationEntity { TenantId = Guid.NewGuid(), Name = "Cancelled save" });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        OperationCanceledException? caughtException = null;
        try
        {
            await context.SaveChangesAsync(cts.Token);
        }
        catch (OperationCanceledException exception)
        {
            caughtException = exception;
        }

        Assert.That(caughtException, Is.Not.Null);
    }

    [Test]
    public async Task BeginTransaction_ShouldHonorCancellationTokenAgainstRealSqlServer()
    {
        await using var context = await SqlServerIntegrationContextFactory.CreateAsync(Guid.NewGuid());
        await using var unitOfWork = new UnitOfWork<SqlServerIntegrationDbContext>(context);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        OperationCanceledException? caughtException = null;
        try
        {
            await unitOfWork.BeginTransactionAsync(cts.Token);
        }
        catch (OperationCanceledException exception)
        {
            caughtException = exception;
        }

        Assert.That(caughtException, Is.Not.Null);
    }

    [Test]
    public async Task ExecuteSql_ShouldHonorCancellationTokenAgainstRealSqlServer()
    {
        await using var context = await SqlServerIntegrationContextFactory.CreateAsync(Guid.NewGuid());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        OperationCanceledException? caughtException = null;
        try
        {
            await context.Database.ExecuteSqlRawAsync("WAITFOR DELAY '00:00:01';", cts.Token);
        }
        catch (OperationCanceledException exception)
        {
            caughtException = exception;
        }

        Assert.That(caughtException, Is.Not.Null);
    }
}
