using System.ComponentModel.DataAnnotations.Schema;
using KUKULCAN.SharedKernel.Database.Extensions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Testcontainers.MsSql;

namespace KUKULCAN.SharedKernel.Database.Integration.SQLServer;

[SetUpFixture]
public sealed class IntegrationTestDatabase
{
    private const string Password = "Kukulcan1!";
    private static MsSqlContainer? _container;

    public static string ConnectionString => _container?.GetConnectionString()
        ?? throw new InvalidOperationException("The SQL Server integration test database has not been initialized.");

    [OneTimeSetUp]
    public async Task SetUpAsync()
    {
        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04")
            .WithPassword(Password)
            .Build();

        await _container.StartAsync();
    }

    [OneTimeTearDown]
    public async Task TearDownAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    internal static async Task<SqlServerDatabaseIntegrationTests.IntegrationDbContext> CreateContextAsync(Guid tenantId)
    {
        var options = Options.Create(new KukulcanDatabaseOptions
        {
            Provider = DatabaseProvider.SqlServer,
            ConnectionString = ConnectionString,
            Retry = new KukulcanDatabaseOptions.RetryOptions { Enabled = false },
            Pool = new KukulcanDatabaseOptions.PoolOptions { Enabled = false },
        });

        var context = new SqlServerDatabaseIntegrationTests.IntegrationDbContext(
            options,
            new SqlServerDatabaseIntegrationTests.IntegrationTenantContext(tenantId),
            new SqlServerDatabaseIntegrationTests.FixedClock(SqlServerDatabaseIntegrationTests.FixedNow),
            Mock.Of<IDomainEventDispatcher>());

        await context.Database.EnsureCreatedAsync();
        return context;
    }
}

[TestFixture]
[NonParallelizable]
public sealed class SqlServerDatabaseIntegrationTests
{
    internal static readonly DateTimeOffset FixedNow =
        new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private IntegrationDbContext _context = null!;
    private Guid _tenantId;

    [SetUp]
    public async Task SetUp()
    {
        _tenantId = Guid.NewGuid();
        _context = await IntegrationTestDatabase.CreateContextAsync(_tenantId);
        await _context.Entities.IgnoreQueryFilters().ExecuteDeleteAsync();
        await _context.ImmutableEntities.IgnoreQueryFilters().ExecuteDeleteAsync();
        await _context.DomainEventEntities.IgnoreQueryFilters().ExecuteDeleteAsync();
    }

    [TearDown]
    public async Task TearDown() => await _context.DisposeAsync();

    [Test]
    public async Task Provider_ShouldUseSqlServerAndPersistData()
    {
        var entity = new IntegrationEntity { TenantId = _tenantId, Name = "SQL Server integration" };
        _context.Entities.Add(entity);
        int affected = await _context.SaveChangesAsync();
        IntegrationEntity persisted = await _context.Entities.SingleAsync(x => x.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_context.Database.ProviderName, Is.EqualTo("Microsoft.EntityFrameworkCore.SqlServer"));
            Assert.That(_context.Database.IsSqlServer(), Is.True);
            Assert.That(affected, Is.EqualTo(1));
            Assert.That(persisted.Name, Is.EqualTo("SQL Server integration"));
            Assert.That(persisted.TenantId, Is.EqualTo(_tenantId));
        }
    }

    [Test]
    public async Task TenantFilter_ShouldIsolateRealDatabaseRows()
    {
        Guid otherTenant = Guid.NewGuid();
        _context.Entities.AddRange(
            new IntegrationEntity { TenantId = _tenantId, Name = "Current tenant" },
            new IntegrationEntity { TenantId = otherTenant, Name = "Other tenant" });
        await _context.SaveChangesAsync();

        List<IntegrationEntity> visible = await _context.Entities.ToListAsync();
        Assert.That(visible.Select(x => x.Name), Is.EqualTo(new[] { "Current tenant" }));
    }

    [Test]
    public async Task TenantModelCache_ShouldKeepTenantModelsIndependentAcrossContexts()
    {
        Guid firstTenant = Guid.NewGuid();
        Guid secondTenant = Guid.NewGuid();
        await using IntegrationDbContext first = await IntegrationTestDatabase.CreateContextAsync(firstTenant);
        await using IntegrationDbContext second = await IntegrationTestDatabase.CreateContextAsync(secondTenant);

        first.Entities.Add(new IntegrationEntity { TenantId = firstTenant, Name = "First tenant" });
        second.Entities.Add(new IntegrationEntity { TenantId = secondTenant, Name = "Second tenant" });
        await first.SaveChangesAsync();
        await second.SaveChangesAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await first.Entities.Select(x => x.Name).ToListAsync(), Is.EqualTo(new[] { "First tenant" }));
            Assert.That(await second.Entities.Select(x => x.Name).ToListAsync(), Is.EqualTo(new[] { "Second tenant" }));
        }
    }

    [Test]
    public async Task SoftDeleteInterceptor_ShouldConvertDeleteIntoLogicalDelete()
    {
        var entity = new IntegrationEntity { TenantId = _tenantId, Name = "To delete" };
        _context.Entities.Add(entity);
        await _context.SaveChangesAsync();
        _context.Entities.Remove(entity);
        await _context.SaveChangesAsync();

        Assert.That(await _context.Entities.AnyAsync(x => x.Id == entity.Id), Is.False);
        IntegrationEntity deleted = await _context.Entities.IgnoreQueryFilters().SingleAsync(x => x.Id == entity.Id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted.IsDeleted, Is.True);
            Assert.That(deleted.DeletedOn, Is.EqualTo(FixedNow));
        }
    }

    [Test]
    public async Task AuditInterceptor_ShouldPersistCreationAndModificationTimestamps()
    {
        var entity = new IntegrationEntity { TenantId = _tenantId, Name = "Audited" };
        _context.Entities.Add(entity);
        await _context.SaveChangesAsync();
        Assert.That(entity.CreatedOn, Is.EqualTo(FixedNow));
        Assert.That(entity.ModifiedOn, Is.Null);
        entity.Name = "Audited updated";
        await _context.SaveChangesAsync();
        Assert.That(entity.ModifiedOn, Is.EqualTo(FixedNow));
    }

    [Test]
    public async Task DomainEventDispatchInterceptor_ShouldDispatchAndClearEventsAfterSuccessfulSave()
    {
        var dispatcher = new Mock<IDomainEventDispatcher>();
        await using var context = CreateContext(_tenantId, dispatcher.Object);
        var entity = new DomainEventEntity { TenantId = _tenantId, Name = "Event source" };
        var domainEvent = new TestDomainEvent(FixedNow);
        entity.AddDomainEventForTest(domainEvent);
        context.DomainEventEntities.Add(entity);
        await context.SaveChangesAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entity.DomainEvents, Is.Empty);
            dispatcher.Verify(x => x.DispatchAsync(domainEvent, It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Test]
    public async Task ImmutableEntityInterceptor_ShouldRejectUpdateAndDelete()
    {
        var entity = new ImmutableIntegrationEntity { TenantId = _tenantId, Name = "Immutable" };
        _context.ImmutableEntities.Add(entity);
        await _context.SaveChangesAsync();
        entity.Name = "Changed";
        InvalidOperationException update = Assert.ThrowsAsync<InvalidOperationException>(async () => await _context.SaveChangesAsync())!;
        Assert.That(update.Message, Does.Contain(nameof(ImmutableIntegrationEntity)));
        _context.ChangeTracker.Clear();
        ImmutableIntegrationEntity persisted = await _context.ImmutableEntities.SingleAsync(x => x.Id == entity.Id);
        _context.ImmutableEntities.Remove(persisted);
        InvalidOperationException delete = Assert.ThrowsAsync<InvalidOperationException>(async () => await _context.SaveChangesAsync())!;
        Assert.That(delete.Message, Does.Contain(nameof(ImmutableIntegrationEntity)));
    }

    [Test]
    public async Task AddKukulcanDbContext_ShouldRegisterContextAndUnitOfWorkAgainstRealSqlServer()
    {
        using ServiceProvider provider = BuildServiceProvider(30, true, new CapturingLogger<SlowQueryInterceptor>());
        await using IntegrationDbContext context = provider.GetRequiredService<IntegrationDbContext>();
        await context.Database.EnsureCreatedAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(context.Database.IsSqlServer(), Is.True);
            Assert.That(provider.GetRequiredService<IUnitOfWork>(), Is.Not.Null);
            Assert.That(provider.GetRequiredService<IOptions<KukulcanDatabaseOptions>>().Value.Retry.Enabled, Is.True);
        }
    }

    [Test]
    public async Task SlowQueryInterceptor_ShouldLogRealSqlServerCommandAboveThreshold()
    {
        int previous = SlowQueryInterceptor.SlowQueryThresholdMs;
        SlowQueryInterceptor.SlowQueryThresholdMs = 0;
        try
        {
            var logger = new CapturingLogger<SlowQueryInterceptor>();
            using ServiceProvider provider = BuildServiceProvider(30, false, logger);
            await using IntegrationDbContext context = provider.GetRequiredService<IntegrationDbContext>();
            await context.Database.ExecuteSqlRawAsync("SELECT 1;");
            Assert.That(logger.WarningMessages, Has.Some.Contains("[SlowQuery]"));
        }
        finally { SlowQueryInterceptor.SlowQueryThresholdMs = previous; }
    }

    [Test]
    public async Task CommandTimeout_ShouldAbortLongRunningRealSqlServerCommand()
    {
        var options = Options.Create(new KukulcanDatabaseOptions
        {
            Provider = DatabaseProvider.SqlServer,
            ConnectionString = IntegrationTestDatabase.ConnectionString,
            CommandTimeoutSeconds = 1,
            Retry = new KukulcanDatabaseOptions.RetryOptions { Enabled = false },
            Pool = new KukulcanDatabaseOptions.PoolOptions { Enabled = false }
        });
        await using var context = new IntegrationDbContext(options, new IntegrationTenantContext(_tenantId), new FixedClock(FixedNow), Mock.Of<IDomainEventDispatcher>());
        await context.Database.EnsureCreatedAsync();
        Exception? exception = null;
        try { await context.Database.ExecuteSqlRawAsync("WAITFOR DELAY '00:00:03';"); }
        catch (Exception ex) { exception = ex; }
        Assert.That(exception, Is.Not.Null);
    }

    [Test]
    public async Task RetryConfiguration_ShouldBeAppliedToRealSqlServerContext()
    {
        var options = Options.Create(new KukulcanDatabaseOptions
        {
            Provider = DatabaseProvider.SqlServer,
            ConnectionString = IntegrationTestDatabase.ConnectionString,
            Retry = new KukulcanDatabaseOptions.RetryOptions { Enabled = true, MaxRetryCount = 2, MaxRetryDelaySeconds = 5 },
            Pool = new KukulcanDatabaseOptions.PoolOptions { Enabled = false }
        });
        await using var context = new IntegrationDbContext(options, new IntegrationTenantContext(_tenantId), new FixedClock(FixedNow), Mock.Of<IDomainEventDispatcher>());
        await context.Database.EnsureCreatedAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(context.Database.IsSqlServer(), Is.True);
            Assert.That(options.Value.Retry.MaxRetryCount, Is.EqualTo(2));
            Assert.That(options.Value.Retry.MaxRetryDelaySeconds, Is.EqualTo(5));
        }
    }

    [Test]
    public async Task UnitOfWork_Commit_ShouldPersistTransaction()
    {
        await using var unitOfWork = new UnitOfWork<IntegrationDbContext>(_context);
        await unitOfWork.BeginTransactionAsync();
        _context.Entities.Add(new IntegrationEntity { TenantId = _tenantId, Name = "Committed" });
        await unitOfWork.CommitTransactionAsync();
        Assert.That(await _context.Entities.AnyAsync(x => x.Name == "Committed"), Is.True);
    }

    [Test]
    public async Task UnitOfWork_Rollback_ShouldNotPersistTransaction()
    {
        await using var unitOfWork = new UnitOfWork<IntegrationDbContext>(_context);
        await unitOfWork.BeginTransactionAsync();
        _context.Entities.Add(new IntegrationEntity { TenantId = _tenantId, Name = "Rolled back" });
        await unitOfWork.RollbackTransactionAsync();
        Assert.That(await _context.Entities.AnyAsync(x => x.Name == "Rolled back"), Is.False);
    }

    [Test]
    public async Task UnitOfWork_FailedCommit_ShouldLeaveTransactionInFailedStateAndNotPersistInvalidOperation()
    {
        await using var unitOfWork = new UnitOfWork<IntegrationDbContext>(_context);
        await unitOfWork.BeginTransactionAsync();
        _context.Entities.Add(new IntegrationEntity { TenantId = _tenantId, Name = "Unique" });
        await _context.SaveChangesAsync();
        _context.Entities.Add(new IntegrationEntity { TenantId = _tenantId, Name = "Unique" });
        Assert.ThrowsAsync<Exception>(async () => await unitOfWork.CommitTransactionAsync());
    }

    [Test] public void ConfigureProvider_ShouldRejectUnsupportedProvider() => Assert.Throws<NotSupportedException>(() => CreateContext((DatabaseProvider)999));
    [Test] public async Task ConfigureProvider_ShouldUseSqlServerWhenProviderInstalled() { await using var context = await IntegrationTestDatabase.CreateContextAsync(Guid.NewGuid()); Assert.That(context.Database.IsSqlServer(), Is.True); }
    [Test] public async Task AddKukulcanDbContext_ShouldBindAllNestedDatabaseOptions() { using var provider = BuildServiceProvider(42, true, new CapturingLogger<SlowQueryInterceptor>()); var value = provider.GetRequiredService<IOptions<KukulcanDatabaseOptions>>().Value; Assert.That(value.CommandTimeoutSeconds, Is.EqualTo(42)); Assert.That(value.Retry.Enabled, Is.True); Assert.That(value.Pool.Enabled, Is.False); }
    [Test] public async Task AddKukulcanDbContext_ShouldBindDatabaseOptionsFromConfiguration() { using var provider = BuildServiceProvider(37, false, new CapturingLogger<SlowQueryInterceptor>()); var value = provider.GetRequiredService<IOptions<KukulcanDatabaseOptions>>().Value; Assert.That(value.CommandTimeoutSeconds, Is.EqualTo(37)); Assert.That(value.Retry.Enabled, Is.False); }
    [Test] public async Task AddKukulcanDbContext_ShouldPreserveDefaultNestedOptionValues() { using var provider = BuildServiceProvider(30, false, new CapturingLogger<SlowQueryInterceptor>()); var value = provider.GetRequiredService<IOptions<KukulcanDatabaseOptions>>().Value; Assert.That(value.Pool.Enabled, Is.False); }
    [Test] public void AddKukulcanDbContext_ShouldRegisterInfrastructureWithExpectedLifetimes() { using var provider = BuildServiceProvider(30, false, new CapturingLogger<SlowQueryInterceptor>()); Assert.That(provider.GetRequiredService<IOptions<KukulcanDatabaseOptions>>(), Is.Not.Null); Assert.That(provider.GetRequiredService<SlowQueryInterceptor>(), Is.Not.Null); }
    [Test] public async Task AddKukulcanDbContext_ShouldRegisterUnitOfWorkAsScopedService() { using var provider = BuildServiceProvider(30, false, new CapturingLogger<SlowQueryInterceptor>()); var first = provider.GetRequiredService<IUnitOfWork>(); var second = provider.GetRequiredService<IUnitOfWork>(); Assert.That(first, Is.Not.Null); Assert.That(second, Is.Not.SameAs(first)); await Task.CompletedTask; }
    [Test] public void AddKukulcanDbContext_ShouldRejectMissingConnectionString() { var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { [$"{KukulcanDatabaseOptions.SectionKey}:Provider"] = nameof(DatabaseProvider.SqlServer) }).Build(); var services = new ServiceCollection(); Assert.Throws<ArgumentException>(() => services.AddKukulcanDbContext<IntegrationDbContext>(configuration)); }
    [Test] public void AddKukulcanDbContext_ShouldRejectWhitespaceConnectionString() { var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { [$"{KukulcanDatabaseOptions.SectionKey}:Provider"] = nameof(DatabaseProvider.SqlServer), [$"{KukulcanDatabaseOptions.SectionKey}:ConnectionString"] = "   " }).Build(); var services = new ServiceCollection(); Assert.Throws<ArgumentException>(() => services.AddKukulcanDbContext<IntegrationDbContext>(configuration)); }
    [Test] public async Task AddKukulcanDbContext_ShouldResolveOneContextPerScope() { using var provider = BuildServiceProvider(30, false, new CapturingLogger<SlowQueryInterceptor>()); using var scope = provider.CreateScope(); var first = scope.ServiceProvider.GetRequiredService<IntegrationDbContext>(); var second = scope.ServiceProvider.GetRequiredService<IntegrationDbContext>(); Assert.That(first, Is.SameAs(second)); await Task.CompletedTask; }
    [Test] public async Task SlowQueryInterceptor_ShouldIncludeSqlWhenSensitiveDataLoggingIsEnabled() { int previous = SlowQueryInterceptor.SlowQueryThresholdMs; SlowQueryInterceptor.SlowQueryThresholdMs = 0; try { var logger = new CapturingLogger<SlowQueryInterceptor>(); using var provider = BuildServiceProvider(30, false, logger, sensitiveDataLogging: true); await using var context = provider.GetRequiredService<IntegrationDbContext>(); await context.Database.ExecuteSqlRawAsync("SELECT 1;"); Assert.That(logger.WarningMessages, Has.Some.Contains("SELECT 1")); } finally { SlowQueryInterceptor.SlowQueryThresholdMs = previous; } }
    [Test] public async Task SlowQueryInterceptor_ShouldLogRealSqlServerReaderCommandAboveThreshold() { int previous = SlowQueryInterceptor.SlowQueryThresholdMs; SlowQueryInterceptor.SlowQueryThresholdMs = -1; try { var logger = new CapturingLogger<SlowQueryInterceptor>(); await using var context = CreateContext(_tenantId, Mock.Of<IDomainEventDispatcher>(), new SlowQueryInterceptor(logger, Options.Create(new KukulcanDatabaseOptions { Provider = DatabaseProvider.SqlServer, ConnectionString = IntegrationTestDatabase.ConnectionString }))); _ = context.Entities.ToList(); Assert.That(logger.WarningMessages, Has.Some.Contains("[SlowQuery]")); } finally { SlowQueryInterceptor.SlowQueryThresholdMs = previous; } }
    [Test] public async Task SlowQueryInterceptor_ShouldNotLogReaderCommandAtOrBelowThreshold() { int previous = SlowQueryInterceptor.SlowQueryThresholdMs; SlowQueryInterceptor.SlowQueryThresholdMs = int.MaxValue; try { var logger = new CapturingLogger<SlowQueryInterceptor>(); await using var context = CreateContext(_tenantId, Mock.Of<IDomainEventDispatcher>(), new SlowQueryInterceptor(logger, Options.Create(new KukulcanDatabaseOptions { Provider = DatabaseProvider.SqlServer, ConnectionString = IntegrationTestDatabase.ConnectionString }))); _ = context.Entities.ToList(); Assert.That(logger.WarningMessages, Is.Empty); } finally { SlowQueryInterceptor.SlowQueryThresholdMs = previous; } }

    [Test] public void TenantModelCacheKeyFactory_ShouldIgnoreTenantForNonKukulcanContext() { using var context = new DbContext(new DbContextOptionsBuilder<DbContext>().Options); object key = new TenantModelCacheKeyFactory().Create(context, false); var tuple = ((Type, Guid?, bool))key; Assert.That(tuple.Item2, Is.Null); }
    [Test] public void TenantModelCacheKeyFactory_ShouldIncludeDesignTimeInCacheKey() { using var context = IntegrationTestDatabaseCreateBare(Guid.NewGuid()); var factory = new TenantModelCacheKeyFactory(); Assert.That(factory.Create(context, false), Is.Not.EqualTo(factory.Create(context, true))); }
    [Test] public void TenantModelCacheKeyFactory_ShouldKeepNonKukulcanDesignTimeKeysDistinct() { using var context = new DbContext(new DbContextOptionsBuilder<DbContext>().Options); var factory = new TenantModelCacheKeyFactory(); Assert.That(factory.Create(context, false), Is.Not.EqualTo(factory.Create(context, true))); }
    [Test] public void TenantModelCacheKeyFactory_ShouldProduceDifferentKeysForDifferentTenants() { using var first = IntegrationTestDatabaseCreateBare(Guid.NewGuid()); using var second = IntegrationTestDatabaseCreateBare(Guid.NewGuid()); var factory = new TenantModelCacheKeyFactory(); Assert.That(factory.Create(first, false), Is.Not.EqualTo(factory.Create(second, false))); }
    [Test] public void TenantModelCacheKeyFactory_ShouldProduceSameKeyForSameTenantAndDesignTime() { Guid tenant = Guid.NewGuid(); using var first = IntegrationTestDatabaseCreateBare(tenant); using var second = IntegrationTestDatabaseCreateBare(tenant); var factory = new TenantModelCacheKeyFactory(); Assert.That(factory.Create(first, false), Is.EqualTo(factory.Create(second, false))); }
    [Test] public void TenantModelCacheKeyFactory_ShouldRejectNullContext() => Assert.Throws<ArgumentNullException>(() => new TenantModelCacheKeyFactory().Create(null!, false));

    [Test] public async Task UnitOfWork_FailedCommit_ShouldRollbackDatabaseTransactionAfterSqlServerConstraintViolation() { await using var unit = new UnitOfWork<IntegrationDbContext>(_context); await unit.BeginTransactionAsync(); _context.Entities.Add(new IntegrationEntity { TenantId = _tenantId, Name = "UniqueSqlServer" }); await _context.SaveChangesAsync(); _context.Entities.Add(new IntegrationEntity { TenantId = _tenantId, Name = "UniqueSqlServer" }); Assert.ThrowsAsync<Exception>(async () => await unit.CommitTransactionAsync()); await unit.DisposeAsync(); }
    [Test] public async Task CommittedChanges_ShouldBecomeVisibleToAnotherRealSqlServerContext() => await AssertCrossContextVisibilityAsync(true);
    [Test] public async Task UncommittedChanges_ShouldRemainInvisibleToAnotherRealSqlServerContext() => await AssertCrossContextVisibilityAsync(false);
    [Test] public async Task UncommittedUpdate_ShouldRemainInvisibleUntilRealSqlServerTransactionCommits() => await AssertCrossContextUpdateVisibilityAsync();
    [Test] public async Task Dispose_ShouldReleaseTransactionAndDiscardSavedChangesAgainstRealSqlServer() { await using var unit = new UnitOfWork<IntegrationDbContext>(_context); await unit.BeginTransactionAsync(); _context.Entities.Add(new IntegrationEntity { TenantId = _tenantId, Name = "Dispose discard" }); await unit.SaveChangesAsync(); unit.Dispose(); Assert.That(await _context.Entities.AnyAsync(x => x.Name == "Dispose discard"), Is.False); }
    [Test] public async Task DisposeAsync_ShouldBeIdempotentWithoutActiveTransactionAgainstRealSqlServer() { var unit = new UnitOfWork<IntegrationDbContext>(_context); await unit.DisposeAsync(); await unit.DisposeAsync(); Assert.Pass(); }
    [Test] public async Task EndTransaction_ShouldDiscardSavedChangesAgainstRealSqlServer() { await using var unit = new UnitOfWork<IntegrationDbContext>(_context); await unit.BeginTransactionAsync(); _context.Entities.Add(new IntegrationEntity { TenantId = _tenantId, Name = "End discard" }); await unit.SaveChangesAsync(); await unit.EndTransactionAsync(); Assert.That(await _context.Entities.AnyAsync(x => x.Name == "End discard"), Is.False); }
    [Test] public async Task CommitTransaction_ShouldHonorCancellationTokenAndReleaseRealSqlServerTransaction() { await using var unit = new UnitOfWork<IntegrationDbContext>(_context); await unit.BeginTransactionAsync(); using var cts = new CancellationTokenSource(); await unit.CommitTransactionAsync(cts.Token); Assert.Pass(); }
    [Test] public async Task EndTransaction_ShouldReleaseTransactionEvenWhenCancellationTokenIsAlreadyCancelled() { await using var unit = new UnitOfWork<IntegrationDbContext>(_context); await unit.BeginTransactionAsync(); using var cts = new CancellationTokenSource(); cts.Cancel(); Assert.DoesNotThrowAsync(async () => await unit.EndTransactionAsync(cts.Token)); }
    [Test] public async Task RollbackTransaction_ShouldHonorCancellationTokenAndReleaseRealSqlServerTransaction() { await using var unit = new UnitOfWork<IntegrationDbContext>(_context); await unit.BeginTransactionAsync(); using var cts = new CancellationTokenSource(); await unit.RollbackTransactionAsync(cts.Token); Assert.Pass(); }
    [Test] public async Task BeginTransaction_ShouldRejectSecondActiveTransactionAgainstRealSqlServer() { await using var unit = new UnitOfWork<IntegrationDbContext>(_context); await unit.BeginTransactionAsync(); Assert.ThrowsAsync<InvalidOperationException>(async () => await unit.BeginTransactionAsync()); await unit.RollbackTransactionAsync(); }
    [Test] public async Task CommitTransaction_ShouldPersistChangesAfterExplicitSaveChangesAgainstRealSqlServer() { await using var unit = new UnitOfWork<IntegrationDbContext>(_context); await unit.BeginTransactionAsync(); _context.Entities.Add(new IntegrationEntity { TenantId = _tenantId, Name = "Explicit save" }); await unit.SaveChangesAsync(); await unit.CommitTransactionAsync(); Assert.That(await _context.Entities.AnyAsync(x => x.Name == "Explicit save"), Is.True); }
    [Test] public void CommitTransaction_ShouldRejectMissingTransaction() { using var unit = new UnitOfWork<IntegrationDbContext>(_context); Assert.ThrowsAsync<InvalidOperationException>(async () => await unit.CommitTransactionAsync()); }
    [Test] public void Dispose_ShouldReleaseActiveRealSqlServerTransactionAndAllowAnotherTransaction() { using var unit = new UnitOfWork<IntegrationDbContext>(_context); Assert.DoesNotThrowAsync(async () => { await unit.BeginTransactionAsync(); unit.Dispose(); await unit.BeginTransactionAsync(); await unit.RollbackTransactionAsync(); }); }
    [Test] public void DisposeAsync_ShouldReleaseActiveRealSqlServerTransactionAndAllowAnotherTransaction() { Assert.DoesNotThrowAsync(async () => { var unit = new UnitOfWork<IntegrationDbContext>(_context); await unit.BeginTransactionAsync(); await unit.DisposeAsync(); await unit.BeginTransactionAsync(); await unit.RollbackTransactionAsync(); }); }
    [Test] public void EndTransaction_ShouldRejectMissingTransaction() { using var unit = new UnitOfWork<IntegrationDbContext>(_context); Assert.ThrowsAsync<InvalidOperationException>(async () => await unit.EndTransactionAsync()); }
    [Test] public async Task EndTransaction_ShouldReleaseRealSqlServerTransactionAndAllowAnotherTransaction() { await using var unit = new UnitOfWork<IntegrationDbContext>(_context); await unit.BeginTransactionAsync(); await unit.EndTransactionAsync(); await unit.BeginTransactionAsync(); await unit.RollbackTransactionAsync(); }
    [Test] public async Task RollbackTransaction_ShouldDiscardPreviouslySavedChangesAgainstRealSqlServer() { await using var unit = new UnitOfWork<IntegrationDbContext>(_context); await unit.BeginTransactionAsync(); _context.Entities.Add(new IntegrationEntity { TenantId = _tenantId, Name = "Rollback saved" }); await unit.SaveChangesAsync(); await unit.RollbackTransactionAsync(); Assert.That(await _context.Entities.AnyAsync(x => x.Name == "Rollback saved"), Is.False); }
    [Test] public void RollbackTransaction_ShouldRejectMissingTransaction() { using var unit = new UnitOfWork<IntegrationDbContext>(_context); Assert.ThrowsAsync<InvalidOperationException>(async () => await unit.RollbackTransactionAsync()); }
    [Test] public async Task SaveChanges_ShouldPersistThroughRealSqlServerUnitOfWork() { await using var unit = new UnitOfWork<IntegrationDbContext>(_context); _context.Entities.Add(new IntegrationEntity { TenantId = _tenantId, Name = "UoW save" }); await unit.SaveChangesAsync(); Assert.That(await _context.Entities.AnyAsync(x => x.Name == "UoW save"), Is.True); }
    [Test] public async Task UnitOfWork_ShouldSupportMultipleConsecutiveRealSqlServerTransactions() { await using var unit = new UnitOfWork<IntegrationDbContext>(_context); await unit.BeginTransactionAsync(); _context.Entities.Add(new IntegrationEntity { TenantId = _tenantId, Name = "Tx1" }); await unit.CommitTransactionAsync(); await unit.BeginTransactionAsync(); _context.Entities.Add(new IntegrationEntity { TenantId = _tenantId, Name = "Tx2" }); await unit.CommitTransactionAsync(); Assert.That(await _context.Entities.CountAsync(x => x.Name == "Tx1" || x.Name == "Tx2"), Is.EqualTo(2)); }

    [Test]
    public async Task AuditInterceptor_ShouldApplySameCreationTimestampToMultipleEntities()
    {
        var first = new IntegrationEntity { TenantId = _tenantId, Name = "Audit-1" };
        var second = new IntegrationEntity { TenantId = _tenantId, Name = "Audit-2" };
        _context.Entities.AddRange(first, second);
        await _context.SaveChangesAsync();
        Assert.That(first.CreatedOn, Is.EqualTo(second.CreatedOn));
    }

    [Test]
    public async Task AuditInterceptor_ShouldUpdateOnlyModifiedEntityTimestamp()
    {
        var first = new IntegrationEntity { TenantId = _tenantId, Name = "Audit-1" };
        var second = new IntegrationEntity { TenantId = _tenantId, Name = "Audit-2" };
        _context.Entities.AddRange(first, second);
        await _context.SaveChangesAsync();
        first.Name = "Audit-1-updated";
        await _context.SaveChangesAsync();
        Assert.That(first.ModifiedOn, Is.EqualTo(FixedNow));
        Assert.That(second.ModifiedOn, Is.Null);
    }

    [Test] public async Task SoftDeleteInterceptor_ShouldApplyAuditMetadataWhenEntityIsDeleted() { var e = new IntegrationEntity { TenantId = _tenantId, Name = "Delete audit" }; _context.Entities.Add(e); await _context.SaveChangesAsync(); _context.Entities.Remove(e); await _context.SaveChangesAsync(); var persisted = await _context.Entities.IgnoreQueryFilters().SingleAsync(x => x.Id == e.Id); Assert.That(persisted.ModifiedOn, Is.EqualTo(FixedNow)); Assert.That(persisted.DeletedOn, Is.EqualTo(FixedNow)); }
    [Test] public async Task SoftDeleteInterceptor_ShouldConvertMultipleDeletesWithoutPhysicalDeletion() { var a = new IntegrationEntity { TenantId = _tenantId, Name = "Delete A" }; var b = new IntegrationEntity { TenantId = _tenantId, Name = "Delete B" }; _context.Entities.AddRange(a,b); await _context.SaveChangesAsync(); _context.Entities.RemoveRange(a,b); await _context.SaveChangesAsync(); Assert.That(await _context.Entities.IgnoreQueryFilters().CountAsync(x => x.Id == a.Id || x.Id == b.Id), Is.EqualTo(2)); }
    [Test] public async Task SoftDeleteInterceptor_ShouldKeepDeletedEntityExcludedByDefaultFilter() { var e = new IntegrationEntity { TenantId = _tenantId, Name = "Excluded" }; _context.Entities.Add(e); await _context.SaveChangesAsync(); _context.Entities.Remove(e); await _context.SaveChangesAsync(); Assert.That(await _context.Entities.AnyAsync(x => x.Id == e.Id), Is.False); }
    [Test] public async Task SoftDeleteInterceptor_ShouldNotAffectEntityWithoutSoftDeleteContract() { var e = new DomainEventEntity { TenantId = _tenantId, Name = "Physical delete" }; _context.DomainEventEntities.Add(e); await _context.SaveChangesAsync(); _context.DomainEventEntities.Remove(e); await _context.SaveChangesAsync(); Assert.That(await _context.DomainEventEntities.IgnoreQueryFilters().AnyAsync(x => x.Id == e.Id), Is.False); }
    [Test] public void ApplySoftDeleteFilter_ShouldRejectNullModelBuilder() => Assert.Throws<ArgumentNullException>(() => ModelBuilderExtensions.ApplySoftDeleteFilter(null!));
    [Test] public async Task KukulcanDbContextBase_ShouldApplyEntityConfigurationsFromDerivedContextAssembly() { await using var context = await IntegrationTestDatabase.CreateContextAsync(Guid.NewGuid()); var entityType = context.Model.FindEntityType(typeof(ConfiguredIntegrationEntity)); Assert.That(entityType, Is.Not.Null); Assert.That(entityType!.GetTableName(), Is.EqualTo("ConfiguredIntegrationEntities")); }
    [Test] public async Task CombinedTenantAndSoftDeleteFilters_ShouldApplyAgainstRealSqlServer() { var visible = new IntegrationEntity { TenantId = _tenantId, Name = "Visible" }; var deleted = new IntegrationEntity { TenantId = _tenantId, Name = "Deleted" }; var other = new IntegrationEntity { TenantId = Guid.NewGuid(), Name = "Other" }; _context.Entities.AddRange(visible,deleted,other); await _context.SaveChangesAsync(); _context.Entities.Remove(deleted); await _context.SaveChangesAsync(); Assert.That(await _context.Entities.Select(x=>x.Name).ToListAsync(), Is.EqualTo(new[]{"Visible"})); }
    [Test] public async Task IgnoreQueryFilters_ShouldExposeDeletedAndOtherTenantRowsAgainstRealSqlServer() { var deleted = new IntegrationEntity { TenantId = _tenantId, Name = "Deleted" }; var other = new IntegrationEntity { TenantId = Guid.NewGuid(), Name = "Other" }; _context.Entities.AddRange(deleted,other); await _context.SaveChangesAsync(); _context.Entities.Remove(deleted); await _context.SaveChangesAsync(); var all = await _context.Entities.IgnoreQueryFilters().OrderBy(x=>x.Name).Select(x=>x.Name).ToListAsync(); Assert.That(all, Is.EqualTo(new[]{"Deleted","Other"})); }
    [Test] public async Task TenantFilter_ShouldApplyToDomainEventEntityAgainstRealSqlServer() { _context.DomainEventEntities.AddRange(new DomainEventEntity{TenantId=_tenantId,Name="Current"}, new DomainEventEntity{TenantId=Guid.NewGuid(),Name="Other"}); await _context.SaveChangesAsync(); Assert.That(await _context.DomainEventEntities.Select(x=>x.Name).ToListAsync(), Is.EqualTo(new[]{"Current"})); }
    [Test] public async Task TenantFilter_ShouldApplyToEntityWithoutSoftDeleteContractAgainstRealSqlServer() { _context.DomainEventEntities.AddRange(new DomainEventEntity{TenantId=_tenantId,Name="Current"}, new DomainEventEntity{TenantId=Guid.NewGuid(),Name="Other"}); await _context.SaveChangesAsync(); Assert.That(await _context.DomainEventEntities.Select(x=>x.Name).ToListAsync(), Is.EqualTo(new[]{"Current"})); }

    [Test] public async Task DomainEventDispatchInterceptor_ShouldDispatchAllEventsFromMultipleAggregatesAgainstRealSqlServer() { var dispatcher = new Mock<IDomainEventDispatcher>(); await using var context = CreateContext(_tenantId, dispatcher.Object); var a = new DomainEventEntity{TenantId=_tenantId,Name="A"}; var b = new DomainEventEntity{TenantId=_tenantId,Name="B"}; var ea=new TestDomainEvent(FixedNow); var eb=new TestDomainEvent(FixedNow); a.AddDomainEventForTest(ea); b.AddDomainEventForTest(eb); context.DomainEventEntities.AddRange(a,b); await context.SaveChangesAsync(); dispatcher.Verify(x=>x.DispatchAsync(ea,It.IsAny<CancellationToken>()),Times.Once); dispatcher.Verify(x=>x.DispatchAsync(eb,It.IsAny<CancellationToken>()),Times.Once); }
    [Test] public async Task DomainEventDispatchInterceptor_ShouldPropagateSaveChangesCancellationTokenAgainstRealSqlServer() { var dispatcher = new Mock<IDomainEventDispatcher>(); CancellationToken token = new CancellationTokenSource().Token; dispatcher.Setup(x=>x.DispatchAsync(It.IsAny<IDomainEvent>(), It.IsAny<CancellationToken>())).Callback<IDomainEvent,CancellationToken>((_, actual)=>Assert.That(actual, Is.EqualTo(token))).Returns(Task.CompletedTask); await using var context = CreateContext(_tenantId, dispatcher.Object); var e = new DomainEventEntity{TenantId=_tenantId,Name="Event"}; e.AddDomainEventForTest(new TestDomainEvent(FixedNow)); context.DomainEventEntities.Add(e); await context.SaveChangesAsync(token); dispatcher.Verify(x=>x.DispatchAsync(It.IsAny<IDomainEvent>(), token), Times.Once); }
    [Test] public async Task ImmutableEntityInterceptor_ShouldAllowInsertAgainstRealSqlServer() { var e=new ImmutableIntegrationEntity{TenantId=_tenantId,Name="Immutable insert"}; _context.ImmutableEntities.Add(e); Assert.That(await _context.SaveChangesAsync(), Is.EqualTo(1)); }
    [Test] public async Task ImmutableEntityInterceptor_ShouldRejectAsyncDeleteAgainstRealSqlServer() { var e=new ImmutableIntegrationEntity{TenantId=_tenantId,Name="Immutable delete"}; _context.ImmutableEntities.Add(e); await _context.SaveChangesAsync(); _context.ImmutableEntities.Remove(e); Assert.ThrowsAsync<InvalidOperationException>(async()=>await _context.SaveChangesAsync()); }
    [Test] public async Task ImmutableEntityInterceptor_ShouldRejectAsyncUpdateAgainstRealSqlServer() { var e=new ImmutableIntegrationEntity{TenantId=_tenantId,Name="Immutable update"}; _context.ImmutableEntities.Add(e); await _context.SaveChangesAsync(); e.Name="Changed"; Assert.ThrowsAsync<InvalidOperationException>(async()=>await _context.SaveChangesAsync()); }
    [Test] public async Task ImmutableEntityInterceptor_ShouldReportAllModifiedImmutableEntitiesAgainstRealSqlServer() { var a=new ImmutableIntegrationEntity{TenantId=_tenantId,Name="A"}; var b=new ImmutableIntegrationEntity{TenantId=_tenantId,Name="B"}; _context.ImmutableEntities.AddRange(a,b); await _context.SaveChangesAsync(); a.Name="A2"; b.Name="B2"; var ex=Assert.ThrowsAsync<InvalidOperationException>(async()=>await _context.SaveChangesAsync())!; Assert.That(ex.Message,Does.Contain(nameof(ImmutableIntegrationEntity))); }

    [Test] public async Task SlowQueryInterceptor_ShouldLogSynchronousReaderCommandAgainstRealSqlServer() { int previous=SlowQueryInterceptor.SlowQueryThresholdMs; SlowQueryInterceptor.SlowQueryThresholdMs=-1; try { var logger=new CapturingLogger<SlowQueryInterceptor>(); var options=Options.Create(new KukulcanDatabaseOptions{Provider=DatabaseProvider.SqlServer,ConnectionString=IntegrationTestDatabase.ConnectionString,CommandTimeoutSeconds=30}); var interceptor=new SlowQueryInterceptor(logger,options); await using var context=CreateContext(_tenantId,Mock.Of<IDomainEventDispatcher>(),interceptor); _=context.Entities.ToList(); Assert.That(logger.WarningMessages,Has.Some.Contains("[SlowQuery]")); } finally { SlowQueryInterceptor.SlowQueryThresholdMs=previous; } }
    [Test] public async Task SlowQueryInterceptor_ShouldLogSynchronousNonQueryCommandAgainstRealSqlServer() { int previous=SlowQueryInterceptor.SlowQueryThresholdMs; SlowQueryInterceptor.SlowQueryThresholdMs=-1; try { var logger=new CapturingLogger<SlowQueryInterceptor>(); var options=Options.Create(new KukulcanDatabaseOptions{Provider=DatabaseProvider.SqlServer,ConnectionString=IntegrationTestDatabase.ConnectionString,CommandTimeoutSeconds=30}); var interceptor=new SlowQueryInterceptor(logger,options); await using var context=CreateContext(_tenantId,Mock.Of<IDomainEventDispatcher>(),interceptor); context.Database.ExecuteSqlRaw("SELECT 1;"); Assert.That(logger.WarningMessages,Has.Some.Contains("[SlowQuery]")); } finally { SlowQueryInterceptor.SlowQueryThresholdMs=previous; } }

    private static IntegrationDbContext CreateContext(Guid tenantId, IDomainEventDispatcher dispatcher, SlowQueryInterceptor? interceptor = null)
    {
        var options = Options.Create(new KukulcanDatabaseOptions { Provider = DatabaseProvider.SqlServer, ConnectionString = IntegrationTestDatabase.ConnectionString, CommandTimeoutSeconds = 30, Retry = new KukulcanDatabaseOptions.RetryOptions { Enabled = false }, Pool = new KukulcanDatabaseOptions.PoolOptions { Enabled = false } });
        return new IntegrationDbContext(options, new IntegrationTenantContext(tenantId), new FixedClock(FixedNow), dispatcher, interceptor);
    }

    private static IntegrationDbContext IntegrationTestDatabaseCreateBare(Guid tenantId) => CreateContext(tenantId, Mock.Of<IDomainEventDispatcher>());
    private static IntegrationDbContext CreateContext(DatabaseProvider provider) { var options=Options.Create(new KukulcanDatabaseOptions{Provider=provider,ConnectionString=IntegrationTestDatabase.ConnectionString,Retry=new KukulcanDatabaseOptions.RetryOptions{Enabled=false},Pool=new KukulcanDatabaseOptions.PoolOptions{Enabled=false}}); return new IntegrationDbContext(options,new IntegrationTenantContext(Guid.NewGuid()),new FixedClock(FixedNow),Mock.Of<IDomainEventDispatcher>()); }

    private static ServiceProvider BuildServiceProvider(int commandTimeoutSeconds, bool retryEnabled, ILogger<SlowQueryInterceptor> logger, bool sensitiveDataLogging=false)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        {
            [$"{KukulcanDatabaseOptions.SectionKey}:Provider"] = nameof(DatabaseProvider.SqlServer),
            [$"{KukulcanDatabaseOptions.SectionKey}:ConnectionString"] = IntegrationTestDatabase.ConnectionString,
            [$"{KukulcanDatabaseOptions.SectionKey}:CommandTimeoutSeconds"] = commandTimeoutSeconds.ToString(),
            [$"{KukulcanDatabaseOptions.SectionKey}:Retry:Enabled"] = retryEnabled.ToString(),
            [$"{KukulcanDatabaseOptions.SectionKey}:Retry:MaxRetryCount"] = "3",
            [$"{KukulcanDatabaseOptions.SectionKey}:Retry:MaxRetryDelaySeconds"] = "5",
            [$"{KukulcanDatabaseOptions.SectionKey}:Pool:Enabled"] = "false",
            [$"{KukulcanDatabaseOptions.SectionKey}:EnableSensitiveDataLogging"] = sensitiveDataLogging.ToString()
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<ITenantContext>(new IntegrationTenantContext(Guid.NewGuid()));
        services.AddSingleton<IClock>(new FixedClock(FixedNow));
        services.AddSingleton<IDomainEventDispatcher>(Mock.Of<IDomainEventDispatcher>());
        services.AddSingleton(logger);
        services.AddKukulcanDbContext<IntegrationDbContext>(configuration);
        return services.BuildServiceProvider();
    }

    private static async Task AssertCrossContextVisibilityAsync(bool commit)
    {
        Guid tenant = Guid.NewGuid(); string name = $"Visibility-{Guid.NewGuid():N}";
        await using var writer = await IntegrationTestDatabase.CreateContextAsync(tenant); await using var reader = await IntegrationTestDatabase.CreateContextAsync(tenant);
        await using var unit = new UnitOfWork<IntegrationDbContext>(writer); await unit.BeginTransactionAsync();
        writer.Entities.Add(new IntegrationEntity{TenantId=tenant,Name=name}); await unit.SaveChangesAsync();
        Assert.That(await reader.Entities.AnyAsync(x=>x.Name==name),Is.False);
        if(commit) { await unit.CommitTransactionAsync(); Assert.That(await reader.Entities.AnyAsync(x=>x.Name==name),Is.True); } else await unit.RollbackTransactionAsync();
    }

    private static async Task AssertCrossContextUpdateVisibilityAsync()
    {
        Guid tenant=Guid.NewGuid(); string original=$"Original-{Guid.NewGuid():N}"; string updated=$"Updated-{Guid.NewGuid():N}";
        await using var setup=await IntegrationTestDatabase.CreateContextAsync(tenant); var e=new IntegrationEntity{TenantId=tenant,Name=original}; setup.Entities.Add(e); await setup.SaveChangesAsync(); int id=e.Id;
        await using var writer=await IntegrationTestDatabase.CreateContextAsync(tenant); await using var reader=await IntegrationTestDatabase.CreateContextAsync(tenant); await using var unit=new UnitOfWork<IntegrationDbContext>(writer); await unit.BeginTransactionAsync();
        var tracked=await writer.Entities.SingleAsync(x=>x.Id==id); tracked.Name=updated; await unit.SaveChangesAsync(); Assert.That((await reader.Entities.SingleAsync(x=>x.Id==id)).Name,Is.EqualTo(original)); await unit.CommitTransactionAsync(); reader.ChangeTracker.Clear(); Assert.That((await reader.Entities.SingleAsync(x=>x.Id==id)).Name,Is.EqualTo(updated));
    }

    internal sealed class IntegrationTenantContext(Guid tenantId) : ITenantContext { public Guid TenantId { get; } = tenantId; }
    internal sealed class FixedClock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow { get; } = now; }

    internal sealed class IntegrationDbContext(IOptions<KukulcanDatabaseOptions> options, ITenantContext tenantContext, IClock clock, IDomainEventDispatcher dispatcher, SlowQueryInterceptor? slowQueryInterceptor=null) : KukulcanDbContextBase(options,tenantContext,clock,dispatcher,slowQueryInterceptor)
    {
        public DbSet<IntegrationEntity> Entities => Set<IntegrationEntity>();
        public DbSet<ImmutableIntegrationEntity> ImmutableEntities => Set<ImmutableIntegrationEntity>();
        public DbSet<DomainEventEntity> DomainEventEntities => Set<DomainEventEntity>();
    }

    internal sealed class IntegrationEntity : IAuditable, ISoftDelete
    {
        public int Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTimeOffset CreatedOn { get; set; }
        public DateTimeOffset? ModifiedOn { get; set; }
        public bool IsDeleted { get; set; }
        public DateTimeOffset? DeletedOn { get; set; }
    }

    internal sealed class ImmutableIntegrationEntity : IImmutable
    {
        public int Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    internal sealed class DomainEventEntity : IHasDomainEvents
    {
        private readonly List<IDomainEvent> _domainEvents = [];
        public int Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = string.Empty;
        public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents;
        public void AddDomainEventForTest(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
        public void ClearDomainEvents() => _domainEvents.Clear();
    }

    internal sealed class ConfiguredIntegrationEntity { public int Id { get; set; } public string Name { get; set; } = string.Empty; }

    internal sealed class ConfiguredIntegrationEntityConfiguration : IEntityTypeConfiguration<ConfiguredIntegrationEntity>
    {
        public void Configure(EntityTypeBuilder<ConfiguredIntegrationEntity> builder)
        {
            builder.ToTable("ConfiguredIntegrationEntities");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        }
    }

    internal sealed record TestDomainEvent(DateTimeOffset OccurredOn) : IDomainEvent;

    internal sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> WarningMessages { get; } = [];
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { if(logLevel==LogLevel.Warning) WarningMessages.Add(formatter(state,exception)); }
        private sealed class NullScope : IDisposable { internal static readonly NullScope Instance = new(); public void Dispose() { } }
    }
}
