using KUKULCAN.SharedKernel.Database.Extensions;

namespace KUKULCAN.SharedKernel.Database.Integration.MySQL;

[TestFixture]
[NonParallelizable]
public sealed class MySqlRemainingIntegrationTests
{
    [Test]
    public void ApplySoftDeleteFilter_ShouldRejectNullModelBuilder()
        => Assert.Throws<ArgumentNullException>(() => ModelBuilderExtensions.ApplySoftDeleteFilter(null!));

    [Test]
    public async Task ModelBuilderFilter_ShouldApplySoftDeleteFilterToModel()
    {
        await using var context = await MySqlIntegrationContextFactory.CreateAsync(Guid.NewGuid());
        var entityType = context.Model.FindEntityType(typeof(MySqlIntegrationEntity));
        Assert.That(entityType?.GetDeclaredQueryFilters(), Is.Not.Empty);
    }

    [Test]
    public async Task ModelBuilderFilter_ShouldApplyTenantFilterToModel()
    {
        await using var context = await MySqlIntegrationContextFactory.CreateAsync(Guid.NewGuid());
        var entityType = context.Model.FindEntityType(typeof(MySqlIntegrationEntity));
        Assert.That(entityType?.GetDeclaredQueryFilters(), Is.Not.Empty);
    }

    [Test]
    public async Task AuditInterceptor_ShouldPersistSynchronousSaveChangesAgainstRealMySql()
    {
        await using var context = await MySqlIntegrationContextFactory.CreateAsync(Guid.NewGuid());
        var entity = new MySqlIntegrationEntity { TenantId = Guid.NewGuid(), Name = "Sync audit" };
        context.Entities.Add(entity);
        Assert.That(context.SaveChanges(), Is.EqualTo(1));
        Assert.That(entity.CreatedOn, Is.EqualTo(MySqlIntegrationConstants.FixedNow));
    }

    [Test]
    public async Task SoftDeleteInterceptor_ShouldHandleSynchronousSaveChangesAgainstRealMySql()
    {
        await using var context = await MySqlIntegrationContextFactory.CreateAsync(Guid.NewGuid());
        var tenantId = Guid.NewGuid();
        var entity = new MySqlIntegrationEntity { TenantId = tenantId, Name = "Sync delete" };
        context.Entities.Add(entity);
        context.SaveChanges();
        context.Entities.Remove(entity);
        context.SaveChanges();
        var deleted = context.Entities.IgnoreQueryFilters().Single(x => x.Id == entity.Id);
        Assert.That(deleted.IsDeleted, Is.True);
    }

    [Test]
    public async Task ImmutableEntityInterceptor_ShouldRejectSynchronousUpdateAgainstRealMySql()
    {
        await using var context = await MySqlIntegrationContextFactory.CreateAsync(Guid.NewGuid());
        var entity = new MySqlImmutableEntity { TenantId = Guid.NewGuid(), Name = "Sync immutable" };
        context.ImmutableEntities.Add(entity);
        context.SaveChanges();
        entity.Name = "Changed";
        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    [Test]
    public async Task DomainEventDispatchInterceptor_ShouldDispatchSynchronouslyAgainstRealMySql()
    {
        var dispatcher = new Mock<IDomainEventDispatcher>();
        await using var context = await MySqlIntegrationContextFactory.CreateAsync(Guid.NewGuid(), dispatcher.Object);
        var entity = new MySqlDomainEventEntity { TenantId = Guid.NewGuid(), Name = "Sync event" };
        var domainEvent = new MySqlTestDomainEvent(MySqlIntegrationConstants.FixedNow);
        entity.AddDomainEventForTest(domainEvent);
        context.DomainEventEntities.Add(entity);
        context.SaveChanges();
        dispatcher.Verify(x => x.DispatchAsync(domainEvent, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public void TenantModelCacheKeyFactory_ShouldKeepNonKukulcanDesignTimeKeysDistinct()
    {
        using var context = new DbContext(new DbContextOptionsBuilder<DbContext>().Options);
        Assert.That(MySqlTenantModelCacheKeyHelper.Create(context, false), Is.Not.EqualTo(MySqlTenantModelCacheKeyHelper.Create(context, true)));
    }
}
