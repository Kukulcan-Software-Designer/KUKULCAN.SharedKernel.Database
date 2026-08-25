using NUnit.Framework;

namespace KUKULCAN.SharedKernel.Database.PostgreSQL.Integration;

[TestFixture]
[NonParallelizable]
public sealed class CancellationIntegrationTests
{
    [Test]
    public async Task SaveChanges_ShouldHonorCancellationTokenAgainstRealPostgreSql()
    {
        Guid tenantId = Guid.NewGuid();
        string name = $"Cancelled-save-{Guid.NewGuid():N}";

        await using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext context =
            await IntegrationTestDatabase.CreateContextAsync(tenantId);

        context.Entities.Add(new PostgreSqlDatabaseIntegrationTests.IntegrationEntity
        {
            TenantId = tenantId,
            Name = name
        });

        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(
            async () => await context.SaveChangesAsync(cancellationTokenSource.Token));

        context.ChangeTracker.Clear();

        Assert.That(
            await context.Entities.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenantId && x.Name == name),
            Is.False);
    }

    [Test]
    public async Task BeginTransaction_ShouldHonorCancellationTokenAgainstRealPostgreSql()
    {
        Guid tenantId = Guid.NewGuid();
        await using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext context =
            await IntegrationTestDatabase.CreateContextAsync(tenantId);

        var unitOfWork = new UnitOfWork<PostgreSqlDatabaseIntegrationTests.IntegrationDbContext>(context);
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(
            async () => await unitOfWork.BeginTransactionAsync(cancellationTokenSource.Token));

        await unitOfWork.DisposeAsync();
    }

    [Test]
    public async Task ExecuteSql_ShouldHonorCancellationTokenAgainstRealPostgreSql()
    {
        Guid tenantId = Guid.NewGuid();
        await using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext context =
            await IntegrationTestDatabase.CreateContextAsync(tenantId);

        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(
            async () => await context.Database.ExecuteSqlRawAsync(
                "SELECT pg_sleep(1);",
                cancellationTokenSource.Token));
    }
}
