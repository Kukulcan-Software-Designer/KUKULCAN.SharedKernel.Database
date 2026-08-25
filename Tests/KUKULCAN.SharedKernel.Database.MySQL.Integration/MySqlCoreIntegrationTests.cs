using MySql.EntityFrameworkCore.Extensions;

namespace KUKULCAN.SharedKernel.Database.Integration.MySQL;

[TestFixture]
[NonParallelizable]
public sealed class MySqlCoreIntegrationTests
{
    private MySqlIntegrationDbContext _context = null!;
    private Guid _tenantId;

    [SetUp]
    public async Task SetUp()
    {
        _tenantId = Guid.NewGuid();
        _context = await MySqlIntegrationContextFactory.CreateAsync(_tenantId);
        await _context.Entities.IgnoreQueryFilters().ExecuteDeleteAsync();
        await _context.ImmutableEntities.IgnoreQueryFilters().ExecuteDeleteAsync();
        await _context.DomainEventEntities.IgnoreQueryFilters().ExecuteDeleteAsync();
    }

    [TearDown]
    public async Task TearDown() => await _context.DisposeAsync();

    [Test]
    public async Task Provider_ShouldUseMySqlAndPersistData()
    {
        var entity = new MySqlIntegrationEntity { TenantId = _tenantId, Name = "MySQL integration" };
        _context.Entities.Add(entity);
        Assert.That(await _context.SaveChangesAsync(), Is.EqualTo(1));
        var persisted = await _context.Entities.SingleAsync(x => x.Id == entity.Id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_context.Database.ProviderName, Is.EqualTo("MySql.EntityFrameworkCore"));
            Assert.That(_context.Database.IsMySql(), Is.True);
            Assert.That(persisted.Name, Is.EqualTo("MySQL integration"));
            Assert.That(persisted.TenantId, Is.EqualTo(_tenantId));
        }
    }

    [Test]
    public async Task TenantFilter_ShouldIsolateRealDatabaseRows()
    {
        _context.Entities.AddRange(
            new MySqlIntegrationEntity { TenantId = _tenantId, Name = "Current tenant" },
            new MySqlIntegrationEntity { TenantId = Guid.NewGuid(), Name = "Other tenant" });
        await _context.SaveChangesAsync();
        Assert.That(await _context.Entities.Select(x => x.Name).ToListAsync(), Is.EqualTo(new[] { "Current tenant" }));
    }

    [Test]
    public async Task TenantModelCache_ShouldKeepTenantModelsIndependentAcrossContexts()
    {
        Guid firstTenant = Guid.NewGuid();
        Guid secondTenant = Guid.NewGuid();
        await using var first = await MySqlIntegrationContextFactory.CreateAsync(firstTenant);
        await using var second = await MySqlIntegrationContextFactory.CreateAsync(secondTenant);
        first.Entities.Add(new MySqlIntegrationEntity { TenantId = firstTenant, Name = "First" });
        second.Entities.Add(new MySqlIntegrationEntity { TenantId = secondTenant, Name = "Second" });
        await first.SaveChangesAsync();
        await second.SaveChangesAsync();
        Assert.That(await first.Entities.Select(x => x.Name).ToListAsync(), Is.EqualTo(new[] { "First" }));
        Assert.That(await second.Entities.Select(x => x.Name).ToListAsync(), Is.EqualTo(new[] { "Second" }));
    }

    [Test]
    public async Task AddKukulcanDbContext_ShouldRegisterContextAndUnitOfWorkAgainstRealMySql()
    {
        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MySqlIntegrationDbContext>();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(context.Database.IsMySql(), Is.True);
            Assert.That(scope.ServiceProvider.GetRequiredService<IUnitOfWork>(), Is.Not.Null);
        }
        await Task.CompletedTask;
    }

    [Test]
    public async Task RetryConfiguration_ShouldBeAppliedToRealMySqlContext()
    {
        using ServiceProvider provider = BuildProvider(retryEnabled: true);
        using IServiceScope scope = provider.CreateScope();
        var value = scope.ServiceProvider.GetRequiredService<IOptions<KukulcanDatabaseOptions>>().Value;
        Assert.That(value.Retry.Enabled, Is.True);
        Assert.That(value.Retry.MaxRetryCount, Is.EqualTo(2));
        await using var context = scope.ServiceProvider.GetRequiredService<MySqlIntegrationDbContext>();
        Assert.That(context.Database.IsMySql(), Is.True);
    }

    [Test]
    public async Task TenantFilter_ShouldApplyToDomainEventEntityAgainstRealMySql()
    {
        _context.DomainEventEntities.AddRange(
            new MySqlDomainEventEntity { TenantId = _tenantId, Name = "Current" },
            new MySqlDomainEventEntity { TenantId = Guid.NewGuid(), Name = "Other" });
        await _context.SaveChangesAsync();
        Assert.That(await _context.DomainEventEntities.Select(x => x.Name).ToListAsync(), Is.EqualTo(new[] { "Current" }));
    }

    [Test]
    public async Task CombinedTenantAndSoftDeleteFilters_ShouldApplyAgainstRealMySql()
    {
        var visible = new MySqlIntegrationEntity { TenantId = _tenantId, Name = "Visible" };
        var deleted = new MySqlIntegrationEntity { TenantId = _tenantId, Name = "Deleted" };
        var other = new MySqlIntegrationEntity { TenantId = Guid.NewGuid(), Name = "Other" };
        _context.Entities.AddRange(visible, deleted, other);
        await _context.SaveChangesAsync();
        _context.Entities.Remove(deleted);
        await _context.SaveChangesAsync();
        Assert.That(await _context.Entities.Select(x => x.Name).ToListAsync(), Is.EqualTo(new[] { "Visible" }));
    }

    [Test]
    public async Task IgnoreQueryFilters_ShouldExposeDeletedAndOtherTenantRowsAgainstRealMySql()
    {
        var deleted = new MySqlIntegrationEntity { TenantId = _tenantId, Name = "Deleted" };
        var other = new MySqlIntegrationEntity { TenantId = Guid.NewGuid(), Name = "Other" };
        _context.Entities.AddRange(deleted, other);
        await _context.SaveChangesAsync();
        _context.Entities.Remove(deleted);
        await _context.SaveChangesAsync();
        Assert.That(await _context.Entities.IgnoreQueryFilters().CountAsync(), Is.EqualTo(2));
    }

    [Test]
    public async Task KukulcanDbContextBase_ShouldApplyEntityConfigurationsFromDerivedContextAssembly()
    {
        var entityType = _context.Model.FindEntityType(typeof(MySqlConfiguredIntegrationEntity));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entityType, Is.Not.Null);
            Assert.That(entityType!.GetTableName(), Is.EqualTo("ConfiguredIntegrationEntities"));
        }
        await Task.CompletedTask;
    }

    [Test]
    public async Task DomainEventDispatchInterceptor_ShouldDispatchAndClearEventsAfterSuccessfulSave()
    {
        var dispatcher = new Mock<IDomainEventDispatcher>();
        await using var context = await MySqlIntegrationContextFactory.CreateAsync(_tenantId, dispatcher.Object);
        var entity = new MySqlDomainEventEntity { TenantId = _tenantId, Name = "Event source" };
        var domainEvent = new MySqlTestDomainEvent(MySqlIntegrationConstants.FixedNow);
        entity.AddDomainEventForTest(domainEvent);
        context.DomainEventEntities.Add(entity);
        await context.SaveChangesAsync();
        Assert.That(entity.DomainEvents, Is.Empty);
        dispatcher.Verify(x => x.DispatchAsync(domainEvent, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task DomainEventDispatchInterceptor_ShouldDispatchAllEventsFromMultipleAggregatesAgainstRealMySql()
    {
        var dispatcher = new Mock<IDomainEventDispatcher>();
        await using var context = await MySqlIntegrationContextFactory.CreateAsync(_tenantId, dispatcher.Object);
        var first = new MySqlDomainEventEntity { TenantId = _tenantId, Name = "First" };
        var second = new MySqlDomainEventEntity { TenantId = _tenantId, Name = "Second" };
        var firstEvent = new MySqlTestDomainEvent(MySqlIntegrationConstants.FixedNow);
        var secondEvent = new MySqlTestDomainEvent(MySqlIntegrationConstants.FixedNow.AddSeconds(1));
        first.AddDomainEventForTest(firstEvent);
        second.AddDomainEventForTest(secondEvent);
        context.DomainEventEntities.AddRange(first, second);
        await context.SaveChangesAsync();
        dispatcher.Verify(x => x.DispatchAsync(firstEvent, It.IsAny<CancellationToken>()), Times.Once);
        dispatcher.Verify(x => x.DispatchAsync(secondEvent, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ImmutableEntityInterceptor_ShouldAllowInsertAgainstRealMySql()
    {
        _context.ImmutableEntities.Add(new MySqlImmutableEntity { TenantId = _tenantId, Name = "Insert" });
        Assert.That(await _context.SaveChangesAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task ImmutableEntityInterceptor_ShouldRejectAsyncUpdateAgainstRealMySql()
    {
        var entity = new MySqlImmutableEntity { TenantId = _tenantId, Name = "Update" };
        _context.ImmutableEntities.Add(entity);
        await _context.SaveChangesAsync();
        entity.Name = "Changed";
        Assert.ThrowsAsync<InvalidOperationException>(async () => await _context.SaveChangesAsync());
    }

    [Test]
    public async Task ImmutableEntityInterceptor_ShouldRejectAsyncDeleteAgainstRealMySql()
    {
        var entity = new MySqlImmutableEntity { TenantId = _tenantId, Name = "Delete" };
        _context.ImmutableEntities.Add(entity);
        await _context.SaveChangesAsync();
        _context.ImmutableEntities.Remove(entity);
        Assert.ThrowsAsync<InvalidOperationException>(async () => await _context.SaveChangesAsync());
    }

    [Test]
    public async Task ImmutableEntityInterceptor_ShouldReportAllModifiedImmutableEntitiesAgainstRealMySql()
    {
        var first = new MySqlImmutableEntity { TenantId = _tenantId, Name = "A" };
        var second = new MySqlImmutableEntity { TenantId = _tenantId, Name = "B" };
        _context.ImmutableEntities.AddRange(first, second);
        await _context.SaveChangesAsync();
        first.Name = "A2";
        second.Name = "B2";
        InvalidOperationException exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await _context.SaveChangesAsync())!;
        Assert.That(exception.Message, Does.Contain(nameof(MySqlImmutableEntity)));
    }

    private static ServiceProvider BuildProvider(bool retryEnabled = false, int timeout = 30, bool sensitive = false)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{KukulcanDatabaseOptions.SectionKey}:Provider"] = nameof(DatabaseProvider.MySql),
            [$"{KukulcanDatabaseOptions.SectionKey}:ConnectionString"] = MySqlIntegrationDatabase.ConnectionString,
            [$"{KukulcanDatabaseOptions.SectionKey}:CommandTimeoutSeconds"] = timeout.ToString(),
            [$"{KukulcanDatabaseOptions.SectionKey}:Retry:Enabled"] = retryEnabled.ToString(),
            [$"{KukulcanDatabaseOptions.SectionKey}:Retry:MaxRetryCount"] = "2",
            [$"{KukulcanDatabaseOptions.SectionKey}:Retry:MaxRetryDelaySeconds"] = "5",
            [$"{KukulcanDatabaseOptions.SectionKey}:Pool:Enabled"] = "false",
            [$"{KukulcanDatabaseOptions.SectionKey}:EnableSensitiveDataLogging"] = sensitive.ToString(),
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<ITenantContext>(new MySqlTenantContext(Guid.NewGuid()));
        services.AddSingleton<IClock>(new FixedClock(MySqlIntegrationConstants.FixedNow));
        services.AddSingleton<IDomainEventDispatcher>(Mock.Of<IDomainEventDispatcher>());
        services.AddLogging();
        services.AddKukulcanDbContext<MySqlIntegrationDbContext>(configuration);
        return services.BuildServiceProvider();
    }
}
