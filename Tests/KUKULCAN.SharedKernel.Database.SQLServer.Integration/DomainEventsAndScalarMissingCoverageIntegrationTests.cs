namespace KUKULCAN.SharedKernel.Database.SQLServer.Integration;

[TestFixture]
[NonParallelizable]
public sealed class DomainEventsAndScalarMissingCoverageIntegrationTests
{
    private Guid _tenantId;

    [SetUp]
    public void SetUp() => _tenantId = Guid.NewGuid();

    [Test]
    public async Task CommitTransaction_ShouldPersistAndDispatchDomainEventFromRealSqlServer()
    {
        var dispatcher = new CapturingDispatcher();
        await using var context = await SqlServerIntegrationContextFactory.CreateAsync(_tenantId, dispatcher);
        await using var verification = await SqlServerIntegrationContextFactory.CreateAsync(_tenantId);
        await using var unit = new UnitOfWork<SqlServerIntegrationDbContext>(context);
        var domainEvent = new SqlServerTestDomainEvent(SqlServerIntegrationConstants.FixedNow);
        var entity = new SqlServerDomainEventEntity { TenantId = _tenantId, Name = "Committed event" };
        entity.AddDomainEventForTest(domainEvent);
        context.DomainEventEntities.Add(entity);

        await unit.BeginTransactionAsync();
        await context.SaveChangesAsync();
        await unit.CommitTransactionAsync();

        verification.ChangeTracker.Clear();
        var persisted = await verification.DomainEventEntities.IgnoreQueryFilters().SingleAsync(x => x.Name == "Committed event");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(persisted.Id, Is.EqualTo(entity.Id));
            Assert.That(dispatcher.Events, Is.EqualTo(new[] { domainEvent }));
            Assert.That(entity.DomainEvents, Is.Empty);
        }
    }

    [Test]
    public async Task CommitTransaction_ShouldDispatchMultipleAggregateEventsOnceAgainstRealSqlServer()
    {
        var dispatcher = new CapturingDispatcher();
        await using var context = await SqlServerIntegrationContextFactory.CreateAsync(_tenantId, dispatcher);
        await using var unit = new UnitOfWork<SqlServerIntegrationDbContext>(context);
        var first = new SqlServerTestDomainEvent(SqlServerIntegrationConstants.FixedNow);
        var second = new SqlServerTestDomainEvent(SqlServerIntegrationConstants.FixedNow.AddSeconds(1));
        var firstEntity = new SqlServerDomainEventEntity { TenantId = _tenantId, Name = "Aggregate A" };
        var secondEntity = new SqlServerDomainEventEntity { TenantId = _tenantId, Name = "Aggregate B" };
        firstEntity.AddDomainEventForTest(first);
        secondEntity.AddDomainEventForTest(second);
        context.DomainEventEntities.AddRange(firstEntity, secondEntity);

        await unit.BeginTransactionAsync();
        await unit.CommitTransactionAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dispatcher.Events, Is.EqualTo(new[] { first, second }));
            Assert.That(firstEntity.DomainEvents, Is.Empty);
            Assert.That(secondEntity.DomainEvents, Is.Empty);
        }
    }

    [Test]
    public async Task CommitFailure_ShouldNotDispatchDomainEventsAgainstRealSqlServer()
    {
        var dispatcher = new CapturingDispatcher();
        await using var context = await SqlServerIntegrationContextFactory.CreateAsync(_tenantId, dispatcher);
        await using var unit = new UnitOfWork<SqlServerIntegrationDbContext>(context);
        var domainEvent = new SqlServerTestDomainEvent(SqlServerIntegrationConstants.FixedNow);
        var entity = new SqlServerDomainEventEntity { TenantId = _tenantId, Name = "Commit failure" };
        entity.AddDomainEventForTest(domainEvent);
        context.DomainEventEntities.Add(entity);

        await unit.BeginTransactionAsync();
        await context.SaveChangesAsync();

        await using (var rollbackCommand = context.Database.GetDbConnection().CreateCommand())
        {
            rollbackCommand.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
            rollbackCommand.CommandText = "ROLLBACK TRANSACTION";
            await rollbackCommand.ExecuteNonQueryAsync();
        }

        Exception? caughtException = null;
        try
        {
            await unit.CommitTransactionAsync();
        }
        catch (Exception exception)
        {
            caughtException = exception;
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(caughtException, Is.Not.Null);
            Assert.That(caughtException, Is.TypeOf<ObjectDisposedException>().Or.TypeOf<InvalidOperationException>());
            Assert.That(dispatcher.Events, Is.Empty);
            Assert.That(entity.DomainEvents, Contains.Item(domainEvent));
        }
    }

    [Test]
    public async Task RollbackTransaction_ShouldClearPendingDispatchStateAndAllowDomainEventRetryAgainstRealSqlServer()
    {
        var dispatcher = new CapturingDispatcher();
        await using var context = await SqlServerIntegrationContextFactory.CreateAsync(_tenantId, dispatcher);
        await using var unit = new UnitOfWork<SqlServerIntegrationDbContext>(context);
        var domainEvent = new SqlServerTestDomainEvent(SqlServerIntegrationConstants.FixedNow);
        var entity = new SqlServerDomainEventEntity { TenantId = _tenantId, Name = "Rollback event" };
        entity.AddDomainEventForTest(domainEvent);
        context.DomainEventEntities.Add(entity);

        await unit.BeginTransactionAsync();
        await context.SaveChangesAsync();
        await unit.RollbackTransactionAsync();

        Assert.That(dispatcher.Events, Is.Empty);
        Assert.That(entity.DomainEvents, Contains.Item(domainEvent));

        await unit.BeginTransactionAsync();
        await unit.CommitTransactionAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dispatcher.Events, Is.EqualTo(new[] { domainEvent }));
            Assert.That(entity.DomainEvents, Is.Empty);
        }
    }

    [Test]
    public async Task DispatcherFailureAfterCommit_ShouldLeaveDatabaseCommittedAndEventPendingAgainstRealSqlServer()
    {
        var dispatcher = new ThrowingDispatcher();
        await using var context = await SqlServerIntegrationContextFactory.CreateAsync(_tenantId, dispatcher);
        await using var verification = await SqlServerIntegrationContextFactory.CreateAsync(_tenantId);
        await using var unit = new UnitOfWork<SqlServerIntegrationDbContext>(context);
        var domainEvent = new SqlServerTestDomainEvent(SqlServerIntegrationConstants.FixedNow);
        var entity = new SqlServerDomainEventEntity { TenantId = _tenantId, Name = "Dispatcher failure" };
        entity.AddDomainEventForTest(domainEvent);
        context.DomainEventEntities.Add(entity);

        await unit.BeginTransactionAsync();
        Assert.ThrowsAsync<InvalidOperationException>(async () => await unit.CommitTransactionAsync());

        verification.ChangeTracker.Clear();
        var persisted = await verification.DomainEventEntities.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Name == "Dispatcher failure");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(persisted, Is.Not.Null);
            Assert.That(entity.DomainEvents, Contains.Item(domainEvent));
        }
    }

    [Test]
    public async Task SlowQueryInterceptor_ScalarExecutedAsync_ShouldLogRealScalarQueryAgainstSqlServer()
    {
        int previousThreshold = SlowQueryInterceptor.SlowQueryThresholdMs;
        SlowQueryInterceptor.SlowQueryThresholdMs = 0;
        try
        {
            var logger = new SqlServerCapturingLogger<SlowQueryInterceptor>();
            var interceptor = new SlowQueryInterceptor(logger, Options.Create(new KukulcanDatabaseOptions()));
            await using var context = await SqlServerIntegrationContextFactory.CreateAsync(_tenantId, slowQueryInterceptor: interceptor);
            await context.Database.OpenConnectionAsync();
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT 1";
            _ = await command.ExecuteScalarAsync();
            await context.Database.CloseConnectionAsync();
            Assert.That(logger.WarningMessages, Has.Some.Contains("[SlowQuery]"));
        }
        finally
        {
            SlowQueryInterceptor.SlowQueryThresholdMs = previousThreshold;
        }
    }

    [Test]
    public async Task SlowQueryInterceptor_ScalarExecuted_ShouldLogRealScalarQueryAgainstSqlServer()
    {
        int previousThreshold = SlowQueryInterceptor.SlowQueryThresholdMs;
        SlowQueryInterceptor.SlowQueryThresholdMs = 0;
        try
        {
            var logger = new SqlServerCapturingLogger<SlowQueryInterceptor>();
            var interceptor = new SlowQueryInterceptor(logger, Options.Create(new KukulcanDatabaseOptions()));
            await using var context = await SqlServerIntegrationContextFactory.CreateAsync(_tenantId, slowQueryInterceptor: interceptor);
            await context.Database.OpenConnectionAsync();
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT 1";
            _ = command.ExecuteScalar();
            await context.Database.CloseConnectionAsync();
            Assert.That(logger.WarningMessages, Has.Some.Contains("[SlowQuery]"));
        }
        finally
        {
            SlowQueryInterceptor.SlowQueryThresholdMs = previousThreshold;
        }
    }

    private sealed class CapturingDispatcher : IDomainEventDispatcher
    {
        public List<IDomainEvent> Events { get; } = [];
        public Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(domainEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingDispatcher : IDomainEventDispatcher
    {
        public Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Dispatcher failure");
    }
}
