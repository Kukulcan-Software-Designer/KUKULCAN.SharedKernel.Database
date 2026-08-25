namespace KUKULCAN.SharedKernel.Database.Integration.SQLServer;

[TestFixture]
[NonParallelizable]
public sealed class DomainEventsAndScalarMissingCoverageIntegrationTests
{
    private SqlServerIntegrationDbContext _context = null!;
    private Guid _tenantId;

    [SetUp]
    public async Task SetUp()
    {
        _tenantId = Guid.NewGuid();
        _context = await SqlServerIntegrationContextFactory.CreateAsync(_tenantId);
        await _context.Entities.IgnoreQueryFilters().ExecuteDeleteAsync();
        await _context.DomainEventEntities.IgnoreQueryFilters().ExecuteDeleteAsync();
    }

    [TearDown]
    public async Task TearDown() => await _context.DisposeAsync();

    [Test]
    public async Task CommitTransaction_ShouldPersistAndDispatchDomainEventFromRealSqlServer()
    {
        var dispatcher = new CapturingDispatcher();
        await using var context = await SqlServerIntegrationContextFactory.CreateAsync(_tenantId, dispatcher);
        await using var verification = await SqlServerIntegrationContextFactory.CreateAsync(Guid.NewGuid());
        var unit = new UnitOfWork<SqlServerIntegrationDbContext>(context);
        var domainEvent = new SqlServerTestDomainEvent();

        await unit.BeginTransactionAsync();
        var entity = new SqlServerDomainEventEntity { TenantId = _tenantId, Name = "Committed event" };
        entity.AddDomainEvent(domainEvent);
        context.DomainEventEntities.Add(entity);
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
        var unit = new UnitOfWork<SqlServerIntegrationDbContext>(context);
        var first = new SqlServerTestDomainEvent();
        var second = new SqlServerTestDomainEvent();

        await unit.BeginTransactionAsync();
        var firstEntity = new SqlServerDomainEventEntity { TenantId = _tenantId, Name = "Aggregate A" };
        firstEntity.AddDomainEvent(first);
        var secondEntity = new SqlServerDomainEventEntity { TenantId = _tenantId, Name = "Aggregate B" };
        secondEntity.AddDomainEvent(second);
        context.DomainEventEntities.AddRange(firstEntity, secondEntity);
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
        var unit = new UnitOfWork<SqlServerIntegrationDbContext>(context);
        var domainEvent = new SqlServerTestDomainEvent();
        var entity = new SqlServerDomainEventEntity { TenantId = _tenantId, Name = "Commit failure" };
        entity.AddDomainEvent(domainEvent);
        context.DomainEventEntities.Add(entity);
        await unit.BeginTransactionAsync();
        await context.SaveChangesAsync();

        context.DomainEventEntities.Remove(entity);
        await context.SaveChangesAsync();

        try
        {
            await unit.CommitTransactionAsync();
            Assert.Fail("CommitTransactionAsync should fail after the transaction state was invalidated.");
        }
        catch (Exception)
        {
            Assert.That(dispatcher.Events, Is.Empty);
        }
    }

    [Test]
    public async Task RollbackTransaction_ShouldNotDispatchDomainEventsAndShouldClearPendingStateAgainstRealSqlServer()
    {
        var dispatcher = new CapturingDispatcher();
        await using var context = await SqlServerIntegrationContextFactory.CreateAsync(_tenantId, dispatcher);
        var unit = new UnitOfWork<SqlServerIntegrationDbContext>(context);
        var domainEvent = new SqlServerTestDomainEvent();
        var entity = new SqlServerDomainEventEntity { TenantId = _tenantId, Name = "Rollback event" };
        entity.AddDomainEvent(domainEvent);
        context.DomainEventEntities.Add(entity);
        await unit.BeginTransactionAsync();
        await context.SaveChangesAsync();
        await unit.RollbackTransactionAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dispatcher.Events, Is.Empty);
            Assert.That(entity.DomainEvents, Is.Empty);
        }
    }

    [Test]
    public async Task DispatcherFailureAfterCommit_ShouldLeaveDatabaseCommittedAndEventPendingAgainstRealSqlServer()
    {
        var dispatcher = new ThrowingDispatcher();
        await using var context = await SqlServerIntegrationContextFactory.CreateAsync(_tenantId, dispatcher);
        await using var verification = await SqlServerIntegrationContextFactory.CreateAsync(Guid.NewGuid());
        var unit = new UnitOfWork<SqlServerIntegrationDbContext>(context);
        var domainEvent = new SqlServerTestDomainEvent();
        var entity = new SqlServerDomainEventEntity { TenantId = _tenantId, Name = "Dispatcher failure" };
        entity.AddDomainEvent(domainEvent);
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
            var logger = new CapturingLogger<SlowQueryInterceptor>();
            await using var context = await SqlServerIntegrationContextFactory.CreateAsync(_tenantId, logger);
            _ = await context.Database.SqlQuery<int>($"SELECT 1 AS Value").SingleAsync();
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

    private sealed record SqlServerTestDomainEvent : IDomainEvent
    {
        public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
    }
}
