namespace KUKULCAN.SharedKernel.Database.Integration.MySQL;

[TestFixture]
[NonParallelizable]
public sealed class MySqlCancellationIntegrationTests
{
    [Test]
    public async Task SaveChanges_ShouldHonorCancellationTokenAgainstRealMySql()
    {
        await using var context = await MySqlIntegrationContextFactory.CreateAsync(Guid.NewGuid());
        context.Entities.Add(new MySqlIntegrationEntity { TenantId = Guid.NewGuid(), Name = "Cancelled save" });
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
    public async Task BeginTransaction_ShouldHonorCancellationTokenAgainstRealMySql()
    {
        await using var context = await MySqlIntegrationContextFactory.CreateAsync(Guid.NewGuid());
        await using var unitOfWork = new UnitOfWork<MySqlIntegrationDbContext>(context);
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
    public async Task ExecuteSql_ShouldHonorCancellationTokenAgainstRealMySql()
    {
        await using var context = await MySqlIntegrationContextFactory.CreateAsync(Guid.NewGuid());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        OperationCanceledException? caughtException = null;
        try
        {
            await context.Database.ExecuteSqlRawAsync("SELECT SLEEP(1);", cts.Token);
        }
        catch (OperationCanceledException exception)
        {
            caughtException = exception;
        }

        Assert.That(caughtException, Is.Not.Null);
    }
}
