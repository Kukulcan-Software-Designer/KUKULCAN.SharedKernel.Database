using KUKULCAN.SharedKernel.Database.Extensions;

namespace KUKULCAN.SharedKernel.Database.Integration.SQLServer;

[TestFixture]
[NonParallelizable]
public sealed class SqlServerRemainingIntegrationTests
{
    [Test]
    public void ApplySoftDeleteFilter_ShouldRejectNullModelBuilder() => Assert.Throws<ArgumentNullException>(() => ModelBuilderExtensions.ApplySoftDeleteFilter(null!));

    [Test]
    public async Task ModelBuilderFilter_ShouldApplySoftDeleteFilterToModel()
    {
        await using var context = await SqlServerIntegrationContextFactory.CreateAsync(Guid.NewGuid());
        var entityType = context.Model.FindEntityType(typeof(SqlServerIntegrationEntity));
        Assert.That(entityType?.GetQueryFilter(), Is.Not.Null);
    }

    [Test]
    public async Task ModelBuilderFilter_ShouldApplyTenantFilterToModel()
    {
        await using var context = await SqlServerIntegrationContextFactory.CreateAsync(Guid.NewGuid());
        var entityType = context.Model.FindEntityType(typeof(SqlServerIntegrationEntity));
        Assert.That(entityType?.GetQueryFilter(), Is.Not.Null);
    }

    [Test]
    public async Task AuditInterceptor_ShouldPersistSynchronousSaveChangesAgainstRealSqlServer()
    {
        await using var context = await SqlServerIntegrationContextFactory.CreateAsync(Guid.NewGuid());
        var entity = new SqlServerIntegrationEntity { TenantId = Guid.NewGuid(), Name = "Sync audit" };
        context.Entities.Add(entity);
        Assert.That(context.SaveChanges(), Is.EqualTo(1));
        Assert.That(entity.CreatedOn, Is.EqualTo(SqlServerIntegrationConstants.FixedNow));
    }

    [Test]
    public async Task SoftDeleteInterceptor_ShouldHandleSynchronousSaveChangesAgainstRealSqlServer()
    {
        await using var context = await SqlServerIntegrationContextFactory.CreateAsync(Guid.NewGuid());
        var tenantId = Guid.NewGuid();
        var entity = new SqlServerIntegrationEntity { TenantId = tenantId, Name = "Sync delete" };
        context.Entities.Add(entity);
        context.SaveChanges();
        context.Entities.Remove(entity);
        context.SaveChanges();
        var deleted = context.Entities.IgnoreQueryFilters().Single(x => x.Id == entity.Id);
        Assert.That(deleted.IsDeleted, Is.True);
    }

    [Test]
    public async Task ImmutableEntityInterceptor_ShouldRejectSynchronousUpdateAgainstRealSqlServer()
    {
        await using var context = await SqlServerIntegrationContextFactory.CreateAsync(Guid.NewGuid());
        var entity = new SqlServerImmutableEntity { TenantId = Guid.NewGuid(), Name = "Sync immutable" };
        context.ImmutableEntities.Add(entity);
        context.SaveChanges();
        entity.Name = "Changed";
        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    [Test]
    public async Task SlowQueryInterceptor_ShouldLogAsynchronousReaderCommandAgainstRealSqlServer()
    {
        var logger = new SqlServerCapturingLogger<SlowQueryInterceptor>();
        int previous = SlowQueryInterceptor.SlowQueryThresholdMs;
        SlowQueryInterceptor.SlowQueryThresholdMs = -1;
        try
        {
            var interceptor = new SlowQueryInterceptor(logger, Options.Create(new KukulcanDatabaseOptions { Provider = DatabaseProvider.SqlServer, ConnectionString = SqlServerIntegrationDatabase.ConnectionString }));
            await using var context = await SqlServerIntegrationContextFactory.CreateAsync(Guid.NewGuid(), slowQueryInterceptor: interceptor);
            _ = await context.Entities.ToListAsync();
            Assert.That(logger.WarningMessages, Has.Some.Contains("[SlowQuery]"));
        }
        finally { SlowQueryInterceptor.SlowQueryThresholdMs = previous; }
    }

    [Test]
    public async Task SlowQueryInterceptor_ShouldLogAsynchronousNonQueryCommandAgainstRealSqlServer()
    {
        var logger = new SqlServerCapturingLogger<SlowQueryInterceptor>();
        int previous = SlowQueryInterceptor.SlowQueryThresholdMs;
        SlowQueryInterceptor.SlowQueryThresholdMs = -1;
        try
        {
            var interceptor = new SlowQueryInterceptor(logger, Options.Create(new KukulcanDatabaseOptions { Provider = DatabaseProvider.SqlServer, ConnectionString = SqlServerIntegrationDatabase.ConnectionString }));
            await using var context = await SqlServerIntegrationContextFactory.CreateAsync(Guid.NewGuid(), slowQueryInterceptor: interceptor);
            await context.Database.ExecuteSqlRawAsync("SELECT 1;");
            Assert.That(logger.WarningMessages, Has.Some.Contains("[SlowQuery]"));
        }
        finally { SlowQueryInterceptor.SlowQueryThresholdMs = previous; }
    }

    [Test]
    public async Task SlowQueryInterceptor_ShouldNotLogAsynchronousNonQueryCommandAtOrBelowThreshold()
    {
        var logger = new SqlServerCapturingLogger<SlowQueryInterceptor>();
        int previous = SlowQueryInterceptor.SlowQueryThresholdMs;
        SlowQueryInterceptor.SlowQueryThresholdMs = int.MaxValue;
        try
        {
            var interceptor = new SlowQueryInterceptor(logger, Options.Create(new KukulcanDatabaseOptions { Provider = DatabaseProvider.SqlServer, ConnectionString = SqlServerIntegrationDatabase.ConnectionString }));
            await using var context = await SqlServerIntegrationContextFactory.CreateAsync(Guid.NewGuid(), slowQueryInterceptor: interceptor);
            await context.Database.ExecuteSqlRawAsync("SELECT 1;");
            Assert.That(logger.WarningMessages, Is.Empty);
        }
        finally { SlowQueryInterceptor.SlowQueryThresholdMs = previous; }
    }

    [Test]
    public async Task DomainEventDispatchInterceptor_ShouldDispatchSynchronouslyAgainstRealSqlServer()
    {
        var dispatcher = new Mock<IDomainEventDispatcher>();
        await using var context = await SqlServerIntegrationContextFactory.CreateAsync(Guid.NewGuid(), dispatcher.Object);
        var entity = new SqlServerDomainEventEntity { TenantId = Guid.NewGuid(), Name = "Sync event" };
        var domainEvent = new SqlServerTestDomainEvent(SqlServerIntegrationConstants.FixedNow);
        entity.AddDomainEventForTest(domainEvent);
        context.DomainEventEntities.Add(entity);
        context.SaveChanges();
        dispatcher.Verify(x => x.DispatchAsync(domainEvent, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task TenantModelCache_ShouldBuildDistinctKeysForTenantsAndReuseKeyForSameTenant()
    {
        Guid tenant = Guid.NewGuid();
        await using var first = await SqlServerIntegrationContextFactory.CreateAsync(tenant);
        await using var second = await SqlServerIntegrationContextFactory.CreateAsync(tenant);
        await using var other = await SqlServerIntegrationContextFactory.CreateAsync(Guid.NewGuid());
        var factory = new TenantModelCacheKeyFactory();
        Assert.That(factory.Create(first, false), Is.EqualTo(factory.Create(second, false)));
        Assert.That(factory.Create(first, false), Is.Not.EqualTo(factory.Create(other, false)));
    }
}
