using NUnit.Framework;

namespace KUKULCAN.SharedKernel.Database.PostgreSQL.Integration;

[TestFixture]
[NonParallelizable]
public sealed class TransactionIsolationIntegrationTests
{
    [Test]
    public async Task UncommittedChanges_ShouldRemainInvisibleToAnotherRealPostgreSqlContext()
    {
        Guid tenantId = Guid.NewGuid();
        string name = $"Isolation-{Guid.NewGuid():N}";

        await using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext writer =
            await IntegrationTestDatabase.CreateContextAsync(tenantId);
        await using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext reader =
            await IntegrationTestDatabase.CreateContextAsync(tenantId);

        var unitOfWork = new UnitOfWork<PostgreSqlDatabaseIntegrationTests.IntegrationDbContext>(writer);
        await unitOfWork.BeginTransactionAsync();

        writer.Entities.Add(new PostgreSqlDatabaseIntegrationTests.IntegrationEntity
        {
            TenantId = tenantId,
            Name = name
        });
        await unitOfWork.SaveChangesAsync();

        Assert.That(
            await reader.Entities.AnyAsync(x => x.Name == name),
            Is.False);

        await unitOfWork.RollbackTransactionAsync();
        await unitOfWork.DisposeAsync();
    }

    [Test]
    public async Task CommittedChanges_ShouldBecomeVisibleToAnotherRealPostgreSqlContext()
    {
        Guid tenantId = Guid.NewGuid();
        string name = $"Commit-Visibility-{Guid.NewGuid():N}";

        await using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext writer =
            await IntegrationTestDatabase.CreateContextAsync(tenantId);
        await using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext reader =
            await IntegrationTestDatabase.CreateContextAsync(tenantId);

        var unitOfWork = new UnitOfWork<PostgreSqlDatabaseIntegrationTests.IntegrationDbContext>(writer);
        await unitOfWork.BeginTransactionAsync();

        writer.Entities.Add(new PostgreSqlDatabaseIntegrationTests.IntegrationEntity
        {
            TenantId = tenantId,
            Name = name
        });
        await unitOfWork.SaveChangesAsync();

        Assert.That(
            await reader.Entities.AnyAsync(x => x.Name == name),
            Is.False);

        await unitOfWork.CommitTransactionAsync();

        Assert.That(
            await reader.Entities.AnyAsync(x => x.Name == name),
            Is.True);

        await unitOfWork.DisposeAsync();
    }

    [Test]
    public async Task UncommittedUpdate_ShouldRemainInvisibleUntilRealPostgreSqlTransactionCommits()
    {
        Guid tenantId = Guid.NewGuid();
        string originalName = $"Original-{Guid.NewGuid():N}";
        string updatedName = $"Updated-{Guid.NewGuid():N}";

        await using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext setup =
            await IntegrationTestDatabase.CreateContextAsync(tenantId);
        var entity = new PostgreSqlDatabaseIntegrationTests.IntegrationEntity
        {
            TenantId = tenantId,
            Name = originalName
        };
        setup.Entities.Add(entity);
        await setup.SaveChangesAsync();
        int entityId = entity.Id;

        await using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext writer =
            await IntegrationTestDatabase.CreateContextAsync(tenantId);
        await using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext reader =
            await IntegrationTestDatabase.CreateContextAsync(tenantId);

        var unitOfWork = new UnitOfWork<PostgreSqlDatabaseIntegrationTests.IntegrationDbContext>(writer);
        await unitOfWork.BeginTransactionAsync();

        PostgreSqlDatabaseIntegrationTests.IntegrationEntity tracked =
            await writer.Entities.SingleAsync(x => x.Id == entityId);
        tracked.Name = updatedName;
        await unitOfWork.SaveChangesAsync();

        PostgreSqlDatabaseIntegrationTests.IntegrationEntity beforeCommit =
            await reader.Entities.SingleAsync(x => x.Id == entityId);

        Assert.That(beforeCommit.Name, Is.EqualTo(originalName));

        await unitOfWork.CommitTransactionAsync();

        reader.ChangeTracker.Clear();
        PostgreSqlDatabaseIntegrationTests.IntegrationEntity afterCommit =
            await reader.Entities.SingleAsync(x => x.Id == entityId);

        Assert.That(afterCommit.Name, Is.EqualTo(updatedName));

        await unitOfWork.DisposeAsync();
    }
}
