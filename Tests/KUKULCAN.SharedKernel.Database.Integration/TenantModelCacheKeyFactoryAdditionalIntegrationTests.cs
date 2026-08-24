using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
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

        IModelCacheKeyFactory factory = firstContext.GetService<IModelCacheKeyFactory>();
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

        IModelCacheKeyFactory factory = firstContext.GetService<IModelCacheKeyFactory>();
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

        IModelCacheKeyFactory factory = context.GetService<IModelCacheKeyFactory>();
        object runtimeKey = factory.Create(context, designTime: false);
        object designTimeKey = factory.Create(context, designTime: true);

        Assert.That(runtimeKey, Is.Not.EqualTo(designTimeKey));
    }

    [Test]
    public void TenantModelCacheKeyFactory_ShouldRejectNullContext()
    {
        var factory = new TenantModelCacheKeyFactory();

        Assert.Throws<ArgumentNullException>(() => factory.Create(null!, designTime: false));
    }

    [Test]
    public void TenantModelCacheKeyFactory_ShouldIgnoreTenantForNonKukulcanContext()
    {
        var factory = new TenantModelCacheKeyFactory();
        using var firstContext = new PlainDbContext();
        using var secondContext = new PlainDbContext();

        object firstKey = factory.Create(firstContext, designTime: false);
        object secondKey = factory.Create(secondContext, designTime: false);

        Assert.That(firstKey, Is.EqualTo(secondKey));
    }

    [Test]
    public void TenantModelCacheKeyFactory_ShouldKeepNonKukulcanDesignTimeKeysDistinct()
    {
        var factory = new TenantModelCacheKeyFactory();
        using var context = new PlainDbContext();

        object runtimeKey = factory.Create(context, designTime: false);
        object designTimeKey = factory.Create(context, designTime: true);

        Assert.That(runtimeKey, Is.Not.EqualTo(designTimeKey));
    }

    private sealed class PlainDbContext : DbContext;
}
