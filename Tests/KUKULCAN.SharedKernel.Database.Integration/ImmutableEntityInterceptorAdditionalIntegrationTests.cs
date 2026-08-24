using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace KUKULCAN.SharedKernel.Database.Integration;

[TestFixture]
[NonParallelizable]
public sealed class ImmutableEntityInterceptorAdditionalIntegrationTests
{
    [Test]
    public async Task ImmutableEntityInterceptor_ShouldRejectAsyncUpdateAgainstRealPostgreSql()
    {
        Guid tenantId = Guid.NewGuid();
        await using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext context =
            await IntegrationTestDatabase.CreateContextAsync(tenantId);

        var entity = new PostgreSqlDatabaseIntegrationTests.ImmutableIntegrationEntity
        {
            TenantId = tenantId,
            Name = "Immutable original"
        };

        context.ImmutableEntities.Add(entity);
        await context.SaveChangesAsync();

        entity.Name = "Immutable changed";

        InvalidOperationException exception =
            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await context.SaveChangesAsync())!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception.Message, Does.Contain("ImmutableIntegrationEntity"));
            Assert.That(exception.Message, Does.Contain("cannot be updated or deleted"));
        }
    }

    [Test]
    public async Task ImmutableEntityInterceptor_ShouldRejectAsyncDeleteAgainstRealPostgreSql()
    {
        Guid tenantId = Guid.NewGuid();
        await using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext context =
            await IntegrationTestDatabase.CreateContextAsync(tenantId);

        var entity = new PostgreSqlDatabaseIntegrationTests.ImmutableIntegrationEntity
        {
            TenantId = tenantId,
            Name = "Immutable delete"
        };

        context.ImmutableEntities.Add(entity);
        await context.SaveChangesAsync();

        context.ImmutableEntities.Remove(entity);

        InvalidOperationException exception =
            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await context.SaveChangesAsync())!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception.Message, Does.Contain("ImmutableIntegrationEntity"));
            Assert.That(exception.Message, Does.Contain("cannot be updated or deleted"));
        }

        context.ChangeTracker.Clear();
        Assert.That(
            await context.ImmutableEntities.IgnoreQueryFilters().AnyAsync(x => x.Id == entity.Id),
            Is.True);
    }

    [Test]
    public async Task ImmutableEntityInterceptor_ShouldReportAllModifiedImmutableEntitiesAgainstRealPostgreSql()
    {
        Guid tenantId = Guid.NewGuid();
        await using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext context =
            await IntegrationTestDatabase.CreateContextAsync(tenantId);

        var first = new PostgreSqlDatabaseIntegrationTests.ImmutableIntegrationEntity
        {
            TenantId = tenantId,
            Name = "First immutable"
        };
        var second = new PostgreSqlDatabaseIntegrationTests.ImmutableIntegrationEntity
        {
            TenantId = tenantId,
            Name = "Second immutable"
        };

        context.ImmutableEntities.AddRange(first, second);
        await context.SaveChangesAsync();

        first.Name = "First changed";
        second.Name = "Second changed";

        InvalidOperationException exception =
            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await context.SaveChangesAsync())!;

        Assert.That(
            exception.Message,
            Does.Contain("ImmutableIntegrationEntity, ImmutableIntegrationEntity"));
    }

    [Test]
    public async Task ImmutableEntityInterceptor_ShouldAllowInsertAgainstRealPostgreSql()
    {
        Guid tenantId = Guid.NewGuid();
        await using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext context =
            await IntegrationTestDatabase.CreateContextAsync(tenantId);

        var entity = new PostgreSqlDatabaseIntegrationTests.ImmutableIntegrationEntity
        {
            TenantId = tenantId,
            Name = "Immutable append only"
        };

        context.ImmutableEntities.Add(entity);
        int affected = await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        PostgreSqlDatabaseIntegrationTests.ImmutableIntegrationEntity persisted =
            await context.ImmutableEntities.SingleAsync(x => x.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(affected, Is.EqualTo(1));
            Assert.That(persisted.Name, Is.EqualTo("Immutable append only"));
            Assert.That(persisted.TenantId, Is.EqualTo(tenantId));
        }
    }
}
