using KUKULCAN.SharedKernel.Database.Integration;
using NUnit.Framework;

namespace KUKULCAN.SharedKernel.Database.PostgreSQL.Integration;

[TestFixture]
[NonParallelizable]
public sealed class AuditAndSoftDeleteAdditionalIntegrationTests
{
    private static readonly DateTimeOffset FixedNow =
        PostgreSqlDatabaseIntegrationTests.FixedNow;

    [Test]
    public async Task AuditInterceptor_ShouldApplySameCreationTimestampToMultipleEntities()
    {
        Guid tenantId = Guid.NewGuid();
        await using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext context =
            await IntegrationTestDatabase.CreateContextAsync(tenantId);

        var first = new PostgreSqlDatabaseIntegrationTests.IntegrationEntity
        {
            TenantId = tenantId,
            Name = "Audit batch one"
        };
        var second = new PostgreSqlDatabaseIntegrationTests.IntegrationEntity
        {
            TenantId = tenantId,
            Name = "Audit batch two"
        };

        context.Entities.AddRange(first, second);
        await context.SaveChangesAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.CreatedOn, Is.EqualTo(FixedNow));
            Assert.That(second.CreatedOn, Is.EqualTo(FixedNow));
            Assert.That(first.ModifiedOn, Is.Null);
            Assert.That(second.ModifiedOn, Is.Null);
        }
    }

    [Test]
    public async Task AuditInterceptor_ShouldUpdateOnlyModifiedEntityTimestamp()
    {
        Guid tenantId = Guid.NewGuid();
        await using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext context =
            await IntegrationTestDatabase.CreateContextAsync(tenantId);

        var modified = new PostgreSqlDatabaseIntegrationTests.IntegrationEntity
        {
            TenantId = tenantId,
            Name = "Will change"
        };
        var unchanged = new PostgreSqlDatabaseIntegrationTests.IntegrationEntity
        {
            TenantId = tenantId,
            Name = "Will remain"
        };

        context.Entities.AddRange(modified, unchanged);
        await context.SaveChangesAsync();

        modified.Name = "Changed";
        await context.SaveChangesAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(modified.ModifiedOn, Is.EqualTo(FixedNow));
            Assert.That(unchanged.ModifiedOn, Is.Null);
            Assert.That(modified.CreatedOn, Is.EqualTo(FixedNow));
            Assert.That(unchanged.CreatedOn, Is.EqualTo(FixedNow));
        }
    }

    [Test]
    public async Task SoftDeleteInterceptor_ShouldApplyAuditMetadataWhenEntityIsDeleted()
    {
        Guid tenantId = Guid.NewGuid();
        await using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext context =
            await IntegrationTestDatabase.CreateContextAsync(tenantId);

        var entity = new PostgreSqlDatabaseIntegrationTests.IntegrationEntity
        {
            TenantId = tenantId,
            Name = "Audited soft delete"
        };

        context.Entities.Add(entity);
        await context.SaveChangesAsync();
        DateTimeOffset createdOn = entity.CreatedOn;

        context.Entities.Remove(entity);
        int affected = await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        PostgreSqlDatabaseIntegrationTests.IntegrationEntity persisted = await context.Entities
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(affected, Is.EqualTo(1));
            Assert.That(persisted.IsDeleted, Is.True);
            Assert.That(persisted.CreatedOn, Is.EqualTo(createdOn));
            Assert.That(persisted.ModifiedOn, Is.EqualTo(FixedNow));
            Assert.That(persisted.DeletedOn, Is.EqualTo(FixedNow));
        }
    }

    [Test]
    public async Task SoftDeleteInterceptor_ShouldConvertMultipleDeletesWithoutPhysicalDeletion()
    {
        Guid tenantId = Guid.NewGuid();
        await using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext context =
            await IntegrationTestDatabase.CreateContextAsync(tenantId);

        var first = new PostgreSqlDatabaseIntegrationTests.IntegrationEntity
        {
            TenantId = tenantId,
            Name = "Delete batch one"
        };
        var second = new PostgreSqlDatabaseIntegrationTests.IntegrationEntity
        {
            TenantId = tenantId,
            Name = "Delete batch two"
        };

        context.Entities.AddRange(first, second);
        await context.SaveChangesAsync();

        context.Entities.RemoveRange(first, second);
        int affected = await context.SaveChangesAsync();

        List<PostgreSqlDatabaseIntegrationTests.IntegrationEntity> persisted =
            await context.Entities
                .IgnoreQueryFilters()
                .Where(x => x.TenantId == tenantId)
                .OrderBy(x => x.Id)
                .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(affected, Is.EqualTo(2));
            Assert.That(persisted, Has.Count.EqualTo(2));
            Assert.That(persisted.All(x => x.IsDeleted), Is.True);
            Assert.That(persisted.All(x => x.DeletedOn == FixedNow), Is.True);
        }
    }

    [Test]
    public async Task SoftDeleteInterceptor_ShouldKeepDeletedEntityExcludedByDefaultFilter()
    {
        Guid tenantId = Guid.NewGuid();
        await using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext context =
            await IntegrationTestDatabase.CreateContextAsync(tenantId);

        var entity = new PostgreSqlDatabaseIntegrationTests.IntegrationEntity
        {
            TenantId = tenantId,
            Name = "Filtered after delete"
        };

        context.Entities.Add(entity);
        await context.SaveChangesAsync();

        context.Entities.Remove(entity);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        bool visible = await context.Entities.AnyAsync(x => x.Id == entity.Id);
        bool existsIgnoringFilters = await context.Entities
            .IgnoreQueryFilters()
            .AnyAsync(x => x.Id == entity.Id && x.IsDeleted);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(visible, Is.False);
            Assert.That(existsIgnoringFilters, Is.True);
        }
    }

    [Test]
    public async Task SoftDeleteInterceptor_ShouldNotAffectEntityWithoutSoftDeleteContract()
    {
        Guid tenantId = Guid.NewGuid();
        await using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext context =
            await IntegrationTestDatabase.CreateContextAsync(tenantId);

        var entity = new PostgreSqlDatabaseIntegrationTests.DomainEventEntity
        {
            TenantId = tenantId,
            Name = "Physical delete"
        };

        context.DomainEventEntities.Add(entity);
        await context.SaveChangesAsync();
        int id = entity.Id;

        context.DomainEventEntities.Remove(entity);
        int affected = await context.SaveChangesAsync();

        bool exists = await context.DomainEventEntities
            .IgnoreQueryFilters()
            .AnyAsync(x => x.Id == id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(affected, Is.EqualTo(1));
            Assert.That(exists, Is.False);
        }
    }
}
