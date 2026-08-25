using NUnit.Framework;

namespace KUKULCAN.SharedKernel.Database.PostgreSQL.Integration;

[TestFixture]
[NonParallelizable]
public sealed class ModelBuilderFilterIntegrationTests
{
    [Test]
    public async Task CombinedTenantAndSoftDeleteFilters_ShouldApplyAgainstRealPostgreSql()
    {
        Guid tenantId = Guid.NewGuid();
        Guid otherTenantId = Guid.NewGuid();

        await using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext context =
            await IntegrationTestDatabase.CreateContextAsync(tenantId);

        var currentTenantActive = new PostgreSqlDatabaseIntegrationTests.IntegrationEntity
        {
            TenantId = tenantId,
            Name = "Current active"
        };
        var currentTenantDeleted = new PostgreSqlDatabaseIntegrationTests.IntegrationEntity
        {
            TenantId = tenantId,
            Name = "Current deleted"
        };
        var otherTenantActive = new PostgreSqlDatabaseIntegrationTests.IntegrationEntity
        {
            TenantId = otherTenantId,
            Name = "Other active"
        };

        context.Entities.AddRange(currentTenantActive, currentTenantDeleted, otherTenantActive);
        await context.SaveChangesAsync();

        currentTenantDeleted.IsDeleted = true;
        currentTenantDeleted.DeletedOn = PostgreSqlDatabaseIntegrationTests.FixedNow;
        await context.SaveChangesAsync();

        List<string> visible = await context.Entities
            .OrderBy(x => x.Name)
            .Select(x => x.Name)
            .ToListAsync();

        Assert.That(visible, Is.EqualTo(new[] { "Current active" }));
    }

    [Test]
    public async Task IgnoreQueryFilters_ShouldExposeDeletedAndOtherTenantRowsAgainstRealPostgreSql()
    {
        Guid tenantId = Guid.NewGuid();
        Guid otherTenantId = Guid.NewGuid();

        await using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext context =
            await IntegrationTestDatabase.CreateContextAsync(tenantId);

        var currentTenantDeleted = new PostgreSqlDatabaseIntegrationTests.IntegrationEntity
        {
            TenantId = tenantId,
            Name = "Current deleted"
        };
        var otherTenantActive = new PostgreSqlDatabaseIntegrationTests.IntegrationEntity
        {
            TenantId = otherTenantId,
            Name = "Other active"
        };

        context.Entities.AddRange(currentTenantDeleted, otherTenantActive);
        await context.SaveChangesAsync();

        currentTenantDeleted.IsDeleted = true;
        currentTenantDeleted.DeletedOn = PostgreSqlDatabaseIntegrationTests.FixedNow;
        await context.SaveChangesAsync();

        List<(Guid TenantId, string Name, bool IsDeleted)> visible = await context.Entities
            .IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId || x.TenantId == otherTenantId)
            .OrderBy(x => x.Name)
            .Select(x => new ValueTuple<Guid, string, bool>(x.TenantId, x.Name, x.IsDeleted))
            .ToListAsync();

        Assert.That(visible, Has.Count.EqualTo(2));
        Assert.That(visible, Does.Contain((tenantId, "Current deleted", true)));
        Assert.That(visible, Does.Contain((otherTenantId, "Other active", false)));
    }

    [Test]
    public async Task TenantFilter_ShouldApplyToEntityWithoutSoftDeleteContractAgainstRealPostgreSql()
    {
        Guid tenantId = Guid.NewGuid();
        Guid otherTenantId = Guid.NewGuid();

        await using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext context =
            await IntegrationTestDatabase.CreateContextAsync(tenantId);

        context.ImmutableEntities.AddRange(
            new PostgreSqlDatabaseIntegrationTests.ImmutableIntegrationEntity
            {
                TenantId = tenantId,
                Name = "Current immutable"
            },
            new PostgreSqlDatabaseIntegrationTests.ImmutableIntegrationEntity
            {
                TenantId = otherTenantId,
                Name = "Other immutable"
            });

        await context.SaveChangesAsync();

        List<string> visible = await context.ImmutableEntities
            .Select(x => x.Name)
            .ToListAsync();

        Assert.That(visible, Is.EqualTo(new[] { "Current immutable" }));
    }

    [Test]
    public async Task TenantFilter_ShouldApplyToDomainEventEntityAgainstRealPostgreSql()
    {
        Guid tenantId = Guid.NewGuid();
        Guid otherTenantId = Guid.NewGuid();

        await using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext context =
            await IntegrationTestDatabase.CreateContextAsync(tenantId);

        context.DomainEventEntities.AddRange(
            new PostgreSqlDatabaseIntegrationTests.DomainEventEntity
            {
                TenantId = tenantId,
                Name = "Current event"
            },
            new PostgreSqlDatabaseIntegrationTests.DomainEventEntity
            {
                TenantId = otherTenantId,
                Name = "Other event"
            });

        await context.SaveChangesAsync();

        List<string> visible = await context.DomainEventEntities
            .Select(x => x.Name)
            .ToListAsync();

        Assert.That(visible, Is.EqualTo(new[] { "Current event" }));
    }
}
