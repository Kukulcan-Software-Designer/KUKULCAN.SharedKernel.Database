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
        IModelCacheKeyFactory factory = new TenantModelCacheKeyFactory();

        Assert.Throws<ArgumentNullException>(() => factory.Create(null!, designTime: false));
    }

    [Test]
    public void TenantModelCacheKeyFactory_ShouldIgnoreTenantForNonKukulcanContext()
    {
        using var firstContext = new PlainDbContext();
        using var secondContext = new PlainDbContext();
        IModelCacheKeyFactory factory = firstContext.GetService<IModelCacheKeyFactory>();

        object firstKey = factory.Create(firstContext, designTime: false);
        object secondKey = factory.Create(secondContext, designTime: false);

        Assert.That(firstKey, Is.EqualTo(secondKey));
    }

    [Test]
    public void TenantModelCacheKeyFactory_ShouldKeepNonKukulcanDesignTimeKeysDistinct()
    {
        using var context = new PlainDbContext();
        IModelCacheKeyFactory factory = context.GetService<IModelCacheKeyFactory>();

        object runtimeKey = factory.Create(context, designTime: false);
        object designTimeKey = factory.Create(context, designTime: true);

        Assert.That(runtimeKey, Is.Not.EqualTo(designTimeKey));
    }

    [Test]
    public async Task TenantModelCache_ShouldBuildDifferentModelsForDifferentTenantsAndReuseModelForSameTenant()
    {
        Guid firstTenantId = Guid.NewGuid();
        Guid secondTenantId = Guid.NewGuid();

        await using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext firstContext =
            await IntegrationTestDatabase.CreateContextAsync(firstTenantId);
        await using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext secondContext =
            await IntegrationTestDatabase.CreateContextAsync(secondTenantId);
        await using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext firstContextAgain =
            await IntegrationTestDatabase.CreateContextAsync(firstTenantId);

        _ = firstContext.Model;
        _ = secondContext.Model;
        _ = firstContextAgain.Model;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstContext.Model, Is.Not.SameAs(secondContext.Model));
            Assert.That(firstContext.Model, Is.SameAs(firstContextAgain.Model));
        }
    }

    private sealed class PlainDbContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseNpgsql(
                "Host=localhost;Port=5432;Database=unused;Username=unused;Password=unused;");
        }
    }
}
