namespace KUKULCAN.SharedKernel.Database.Integration.SQLServer;

[TestFixture]
[NonParallelizable]
public sealed class SqlServerCoreIntegrationTests
{
    private SqlServerIntegrationDbContext _context = null!;
    private Guid _tenantId;

    [SetUp]
    public async Task SetUp()
    {
        _tenantId = Guid.NewGuid();
        _context = await SqlServerIntegrationContextFactory.CreateAsync(_tenantId);
        await _context.Entities.IgnoreQueryFilters().ExecuteDeleteAsync();
        await _context.ImmutableEntities.IgnoreQueryFilters().ExecuteDeleteAsync();
        await _context.DomainEventEntities.IgnoreQueryFilters().ExecuteDeleteAsync();
    }

    [TearDown]
    public async Task TearDown() => await _context.DisposeAsync();

    [Test]
    public async Task Provider_ShouldUseSqlServerAndPersistData()
    {
        var entity = new SqlServerIntegrationEntity { TenantId = _tenantId, Name = "SQL Server integration" };
        _context.Entities.Add(entity);
        Assert.That(await _context.SaveChangesAsync(), Is.EqualTo(1));
        var persisted = await _context.Entities.SingleAsync(x => x.Id == entity.Id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_context.Database.ProviderName, Is.EqualTo("Microsoft.EntityFrameworkCore.SqlServer"));
            Assert.That(_context.Database.IsSqlServer(), Is.True);
            Assert.That(persisted.Name, Is.EqualTo("SQL Server integration"));
            Assert.That(persisted.TenantId, Is.EqualTo(_tenantId));
        }
    }

    [Test]
    public async Task TenantFilter_ShouldIsolateRealDatabaseRows()
    {
        _context.Entities.AddRange(
            new SqlServerIntegrationEntity { TenantId = _tenantId, Name = "Current tenant" },
            new SqlServerIntegrationEntity { TenantId = Guid.NewGuid(), Name = "Other tenant" });
        await _context.SaveChangesAsync();
        Assert.That(await _context.Entities.Select(x => x.Name).ToListAsync(), Is.EqualTo(new[] { "Current tenant" }));
    }

    [Test]
    public async Task TenantModelCache_ShouldKeepTenantModelsIndependentAcrossContexts()
    {
        Guid firstTenant = Guid.NewGuid();
        Guid secondTenant = Guid.NewGuid();
        await using var first = await SqlServerIntegrationContextFactory.CreateAsync(firstTenant);
        await using var second = await SqlServerIntegrationContextFactory.CreateAsync(secondTenant);
        first.Entities.Add(new SqlServerIntegrationEntity { TenantId = firstTenant, Name = "First" });
        second.Entities.Add(new SqlServerIntegrationEntity { TenantId = secondTenant, Name = "Second" });
        await first.SaveChangesAsync();
        await second.SaveChangesAsync();
        Assert.That(await first.Entities.Select(x => x.Name).ToListAsync(), Is.EqualTo(new[] { "First" }));
        Assert.That(await second.Entities.Select(x => x.Name).ToListAsync(), Is.EqualTo(new[] { "Second" }));
    }

    [Test]
    public async Task AddKukulcanDbContext_ShouldRegisterContextAndUnitOfWorkAgainstRealSqlServer()
    {
        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SqlServerIntegrationDbContext>();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(context.Database.IsSqlServer(), Is.True);
            Assert.That(scope.ServiceProvider.GetRequiredService<IUnitOfWork>(), Is.Not.Null);
        }
        await Task.CompletedTask;
    }

    [Test]
    public async Task RetryConfiguration_ShouldBeAppliedToRealSqlServerContext()
    {
        using ServiceProvider provider = BuildProvider(retryEnabled: true);
        using IServiceScope scope = provider.CreateScope();
        var value = scope.ServiceProvider.GetRequiredService<IOptions<KukulcanDatabaseOptions>>().Value;
        Assert.That(value.Retry.Enabled, Is.True);
        Assert.That(value.Retry.MaxRetryCount, Is.EqualTo(2));
        await using var context = scope.ServiceProvider.GetRequiredService<SqlServerIntegrationDbContext>();
        Assert.That(context.Database.IsSqlServer(), Is.True);
    }

    [Test]
    public async Task TenantFilter_ShouldApplyToDomainEventEntityAgainstRealSqlServer()
    {
        _context.DomainEventEntities.AddRange(
            new SqlServerDomainEventEntity { TenantId = _tenantId, Name = "Current" },
            new SqlServerDomainEventEntity { TenantId = Guid.NewGuid(), Name = "Other" });
        await _context.SaveChangesAsync();
        Assert.That(await _context.DomainEventEntities.Select(x => x.Name).ToListAsync(), Is.EqualTo(new[] { "Current" }));
    }

    [Test]
    public async Task TenantFilter_ShouldApplyToEntityWithoutSoftDeleteContractAgainstRealSqlServer()
    {
        _context.DomainEventEntities.AddRange(
            new SqlServerDomainEventEntity { TenantId = _tenantId, Name = "Current" },
            new SqlServerDomainEventEntity { TenantId = Guid.NewGuid(), Name = "Other" });
        await _context.SaveChangesAsync();
        Assert.That(await _context.DomainEventEntities.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task CombinedTenantAndSoftDeleteFilters_ShouldApplyAgainstRealSqlServer()
    {
        var visible = new SqlServerIntegrationEntity { TenantId = _tenantId, Name = "Visible" };
        var deleted = new SqlServerIntegrationEntity { TenantId = _tenantId, Name = "Deleted" };
        var other = new SqlServerIntegrationEntity { TenantId = Guid.NewGuid(), Name = "Other" };
        _context.Entities.AddRange(visible, deleted, other);
        await _context.SaveChangesAsync();
        _context.Entities.Remove(deleted);
        await _context.SaveChangesAsync();
        Assert.That(await _context.Entities.Select(x => x.Name).ToListAsync(), Is.EqualTo(new[] { "Visible" }));
    }

    [Test]
    public async Task IgnoreQueryFilters_ShouldExposeDeletedAndOtherTenantRowsAgainstRealSqlServer()
    {
        var deleted = new SqlServerIntegrationEntity { TenantId = _tenantId, Name = "Deleted" };
        var other = new SqlServerIntegrationEntity { TenantId = Guid.NewGuid(), Name = "Other" };
        _context.Entities.AddRange(deleted, other);
        await _context.SaveChangesAsync();
        _context.Entities.Remove(deleted);
        await _context.SaveChangesAsync();
        Assert.That(await _context.Entities.IgnoreQueryFilters().CountAsync(), Is.EqualTo(2));
    }

    [Test]
    public async Task KukulcanDbContextBase_ShouldApplyEntityConfigurationsFromDerivedContextAssembly()
    {
        var entityType = _context.Model.FindEntityType(typeof(SqlServerConfiguredIntegrationEntity));
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
        await using var context = await SqlServerIntegrationContextFactory.CreateAsync(_tenantId, dispatcher.Object);
        var entity = new SqlServerDomainEventEntity { TenantId = _tenantId, Name = "Event source" };
        var domainEvent = new SqlServerTestDomainEvent(SqlServerIntegrationConstants.FixedNow);
        entity.AddDomainEventForTest(domainEvent);
        context.DomainEventEntities.Add(entity);
        await context.SaveChangesAsync();
        Assert.That(entity.DomainEvents, Is.Empty);
        dispatcher.Verify(x => x.DispatchAsync(domainEvent, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task DomainEventDispatchInterceptor_ShouldDispatchAllEventsFromMultipleAggregatesAgainstRealSqlServer()
    {
        var dispatcher = new Mock<IDomainEventDispatcher>();
        await using var context = await SqlServerIntegrationContextFactory.CreateAsync(_tenantId, dispatcher.Object);
        var first = new SqlServerDomainEventEntity { TenantId = _tenantId, Name = "First" };
        var second = new SqlServerDomainEventEntity { TenantId = _tenantId, Name = "Second" };
        var firstEvent = new SqlServerTestDomainEvent(SqlServerIntegrationConstants.FixedNow);
        var secondEvent = new SqlServerTestDomainEvent(SqlServerIntegrationConstants.FixedNow);
        first.AddDomainEventForTest(firstEvent);
        second.AddDomainEventForTest(secondEvent);
        context.DomainEventEntities.AddRange(first, second);
        context.SaveChanges();
        dispatcher.Verify(x => x.DispatchAsync(firstEvent, It.IsAny<CancellationToken>()), Times.Once);
        dispatcher.Verify(x => x.DispatchAsync(secondEvent, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task DomainEventDispatchInterceptor_ShouldPropagateSaveChangesCancellationTokenAgainstRealSqlServer()
    {
        var dispatcher = new Mock<IDomainEventDispatcher>();
        CancellationToken token = new CancellationTokenSource().Token;
        dispatcher.Setup(x => x.DispatchAsync(It.IsAny<IDomainEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback<IDomainEvent, CancellationToken>((_, actual) => Assert.That(actual, Is.EqualTo(token)));
        await using var context = await SqlServerIntegrationContextFactory.CreateAsync(_tenantId, dispatcher.Object);
        var entity = new SqlServerDomainEventEntity { TenantId = _tenantId, Name = "Event source" };
        entity.AddDomainEventForTest(new SqlServerTestDomainEvent(SqlServerIntegrationConstants.FixedNow));
        context.DomainEventEntities.Add(entity);
        await context.SaveChangesAsync(token);
        dispatcher.Verify(x => x.DispatchAsync(It.IsAny<IDomainEvent>(), token), Times.Once);
    }

    [Test]
    public async Task ImmutableEntityInterceptor_ShouldAllowInsertAgainstRealSqlServer()
    {
        _context.ImmutableEntities.Add(new SqlServerImmutableEntity { TenantId = _tenantId, Name = "Insert" });
        Assert.That(await _context.SaveChangesAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task ImmutableEntityInterceptor_ShouldRejectAsyncUpdateAgainstRealSqlServer()
    {
        var entity = new SqlServerImmutableEntity { TenantId = _tenantId, Name = "Update" };
        _context.ImmutableEntities.Add(entity);
        await _context.SaveChangesAsync();
        entity.Name = "Changed";
        Assert.ThrowsAsync<InvalidOperationException>(async () => await _context.SaveChangesAsync());
    }

    [Test]
    public async Task ImmutableEntityInterceptor_ShouldRejectAsyncDeleteAgainstRealSqlServer()
    {
        var entity = new SqlServerImmutableEntity { TenantId = _tenantId, Name = "Delete" };
        _context.ImmutableEntities.Add(entity);
        await _context.SaveChangesAsync();
        _context.ImmutableEntities.Remove(entity);
        Assert.ThrowsAsync<InvalidOperationException>(async () => await _context.SaveChangesAsync());
    }

    [Test]
    public async Task ImmutableEntityInterceptor_ShouldReportAllModifiedImmutableEntitiesAgainstRealSqlServer()
    {
        var first = new SqlServerImmutableEntity { TenantId = _tenantId, Name = "A" };
        var second = new SqlServerImmutableEntity { TenantId = _tenantId, Name = "B" };
        _context.ImmutableEntities.AddRange(first, second);
        await _context.SaveChangesAsync();
        first.Name = "A2";
        second.Name = "B2";
        InvalidOperationException exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await _context.SaveChangesAsync())!;
        Assert.That(exception.Message, Does.Contain(nameof(SqlServerImmutableEntity)));
    }

    private static ServiceProvider BuildProvider(bool retryEnabled = false, int timeout = 30, bool sensitive = false)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{KukulcanDatabaseOptions.SectionKey}:Provider"] = nameof(DatabaseProvider.SqlServer),
            [$"{KukulcanDatabaseOptions.SectionKey}:ConnectionString"] = SqlServerIntegrationDatabase.ConnectionString,
            [$"{KukulcanDatabaseOptions.SectionKey}:CommandTimeoutSeconds"] = timeout.ToString(),
            [$"{KukulcanDatabaseOptions.SectionKey}:Retry:Enabled"] = retryEnabled.ToString(),
            [$"{KukulcanDatabaseOptions.SectionKey}:Retry:MaxRetryCount"] = "2",
            [$"{KukulcanDatabaseOptions.SectionKey}:Retry:MaxRetryDelaySeconds"] = "5",
            [$"{KukulcanDatabaseOptions.SectionKey}:Pool:Enabled"] = "false",
            [$"{KukulcanDatabaseOptions.SectionKey}:EnableSensitiveDataLogging"] = sensitive.ToString(),
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<ITenantContext>(new SqlServerTenantContext(Guid.NewGuid()));
        services.AddSingleton<IClock>(new FixedClock(SqlServerIntegrationConstants.FixedNow));
        services.AddSingleton<IDomainEventDispatcher>(Mock.Of<IDomainEventDispatcher>());
        services.AddLogging();
        services.AddKukulcanDbContext<SqlServerIntegrationDbContext>(configuration);
        return services.BuildServiceProvider();
    }
}
