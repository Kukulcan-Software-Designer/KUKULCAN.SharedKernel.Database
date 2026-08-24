using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace KUKULCAN.SharedKernel.Database.Integration;

[TestFixture]
[NonParallelizable]
public sealed class TenantModelCacheKeyFactoryAdditionalIntegrationTests
{
    [Test]
    public async Task TenantModelCacheKeyFactory_ShouldProduceDifferentKeysForDifferentTenants()
    {
        Guid firstTenantId = Guid.NewGuid();
        Guid secondTenantId = Guid.NewGuid();

        await using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext firstContext =
            await IntegrationTestDatabase.CreateContextAsync(firstTenantId);
        await using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext secondContext =
            await IntegrationTestDatabase.CreateContextAsync(secondTenantId);

        var factory = new TenantModelCacheKeyFactory();
        object firstKey = factory.Create(firstContext, designTime: false);
        object secondKey = factory.Create(secondContext, designTime: false);

        Assert.That(firstKey, Is.Not.EqualTo(secondKey));
    }

    [Test]
    public async Task TenantModelCacheKeyFactory_ShouldProduceSameKeyForSameTenantAndDesignTime()
    {
        Guid tenantId = Guid.NewGuid();

        await using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext firstContext =
            await IntegrationTestDatabase.CreateContextAsync(tenantId);
        await using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext secondContext =
            await IntegrationTestDatabase.CreateContextAsync(tenantId);

        var factory = new TenantModelCacheKeyFactory();
        object firstKey = factory.Create(firstContext, designTime: false);
        object secondKey = factory.Create(secondContext, designTime: false);

        Assert.That(firstKey, Is.EqualTo(secondKey));
    }

    [Test]
    public async Task TenantModelCacheKeyFactory_ShouldIncludeDesignTimeInCacheKey()
    {
        Guid tenantId = Guid.NewGuid();

        await using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext context =
            await IntegrationTestDatabase.CreateContextAsync(tenantId);

        var factory = new TenantModelCacheKeyFactory();
        object runtimeKey = factory.Create(context, designTime: false);
        object designTimeKey = factory.Create(context, designTime: true);

        Assert.That(runtimeKey, Is.Not.EqualTo(designTimeKey));
    }
}
