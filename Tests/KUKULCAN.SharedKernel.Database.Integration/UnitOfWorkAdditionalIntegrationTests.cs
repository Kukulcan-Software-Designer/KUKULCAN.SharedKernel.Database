using KUKULCAN.SharedKernel.Database.UnitOfWork;
using NUnit.Framework;

namespace KUKULCAN.SharedKernel.Database.Integration;

[TestFixture]
[NonParallelizable]
public sealed class UnitOfWorkAdditionalIntegrationTests
{
    [Test]
    public async Task EndTransaction_ShouldDiscardSavedChangesAgainstRealPostgreSql()
    {
        Guid tenantId = Guid.NewGuid();
        await using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext context =
            await IntegrationTestDatabase.CreateContextAsync(tenantId);
        var unitOfWork = new UnitOfWork<PostgreSqlDatabaseIntegrationTests.IntegrationDbContext>(context);

        await unitOfWork.BeginTransactionAsync();
        context.Entities.Add(new PostgreSqlDatabaseIntegrationTests.IntegrationEntity
        {
            TenantId = tenantId,
            Name = "Ended transaction"
        });
        await unitOfWork.SaveChangesAsync();
        await unitOfWork.EndTransactionAsync();

        await using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext verificationContext =
            await IntegrationTestDatabase.CreateContextAsync(tenantId);

        Assert.That(
            await verificationContext.Entities.IgnoreQueryFilters().AnyAsync(x => x.Name == "Ended transaction"),
            Is.False);
    }

    [Test]
    public async Task Dispose_ShouldReleaseTransactionAndDiscardSavedChangesAgainstRealPostgreSql()
    {
        Guid tenantId = Guid.NewGuid();
        await using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext context =
            await IntegrationTestDatabase.CreateContextAsync(tenantId);
        var unitOfWork = new UnitOfWork<PostgreSqlDatabaseIntegrationTests.IntegrationDbContext>(context);

        await unitOfWork.BeginTransactionAsync();
        context.Entities.Add(new PostgreSqlDatabaseIntegrationTests.IntegrationEntity
        {
            TenantId = tenantId,
            Name = "Disposed transaction"
        });
        await unitOfWork.SaveChangesAsync();
        unitOfWork.Dispose();

        await unitOfWork.BeginTransactionAsync();
        await unitOfWork.RollbackTransactionAsync();

        await using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext verificationContext =
            await IntegrationTestDatabase.CreateContextAsync(tenantId);

        Assert.That(
            await verificationContext.Entities.IgnoreQueryFilters().AnyAsync(x => x.Name == "Disposed transaction"),
            Is.False);
    }

    [Test]
    public async Task DisposeAsync_ShouldBeIdempotentWithoutActiveTransactionAgainstRealPostgreSql()
    {
        Guid tenantId = Guid.NewGuid();
        await using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext context =
            await IntegrationTestDatabase.CreateContextAsync(tenantId);
        var unitOfWork = new UnitOfWork<PostgreSqlDatabaseIntegrationTests.IntegrationDbContext>(context);

        await unitOfWork.DisposeAsync();
        await unitOfWork.DisposeAsync();
        await unitOfWork.BeginTransactionAsync();
        await unitOfWork.RollbackTransactionAsync();

        Assert.Pass();
    }
}
