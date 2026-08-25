namespace KUKULCAN.SharedKernel.Database.Integration.MySQL;

[TestFixture]
[NonParallelizable]
public sealed class DomainEventsAndScalarMissingCoverageIntegrationTests
{
    private Guid _tenantId;

    [SetUp]
    public void SetUp() => _tenantId = Guid.NewGuid();

    [Test]
    public async Task CommitTransaction_ShouldPersistAndDispatchDomainEventFromRealMySql()
    {
        var dispatcher = new CapturingDispatcher();
        await using var context = await MySqlIntegrationContextFactory.CreateAsync(_tenantId, dispatcher);
        await using var verification = await MySqlIntegrationContextFactory.CreateAsync(_tenantId);
        await using var unit = new UnitOfWork<MySqlIntegrationDbContext>(context);
        var domainEvent = new MySqlTestDomainEvent(MySqlIntegrationConstants.FixedNow);
        var entity = new MySqlDomainEventEntity { TenantId = _tenantId, Name = "Committed event" };
        entity.AddDomainEventForTest(domainEvent);
        context.DomainEventEntities.Add(entity);
        await unit.BeginTransactionAsync();
        await unit.CommitTransactionAsync();
        var persisted = await verification.DomainEventEntities.IgnoreQueryFilters().SingleAsync(x => x.Name == "Committed event");
        Assert.That(persisted.Id, Is.EqualTo(entity.Id));
        Assert.That(dispatcher.Events, Is.EqualTo(new IDomainEvent[] { domainEvent }));
        Assert.That(entity.DomainEvents, Is.Empty);
    }

    [Test]
    public async Task CommitTransaction_ShouldDispatchMultipleAggregateEventsOnceAgainstRealMySql()
    {
        var dispatcher = new CapturingDispatcher();
        await using var context = await MySqlIntegrationContextFactory.CreateAsync(_tenantId, dispatcher);
        await using var unit = new UnitOfWork<MySqlIntegrationDbContext>(context);
        var first = new MySqlTestDomainEvent(MySqlIntegrationConstants.FixedNow);
        var second = new MySqlTestDomainEvent(MySqlIntegrationConstants.FixedNow.AddSeconds(1));
        var firstEntity = new MySqlDomainEventEntity { TenantId = _tenantId, Name = "Aggregate A" };
        var secondEntity = new MySqlDomainEventEntity { TenantId = _tenantId, Name = "Aggregate B" };
        firstEntity.AddDomainEventForTest(first);
        secondEntity.AddDomainEventForTest(second);
        context.DomainEventEntities.AddRange(firstEntity, secondEntity);
        await unit.BeginTransactionAsync();
        await unit.CommitTransactionAsync();
        Assert.That(dispatcher.Events, Is.EqualTo(new IDomainEvent[] { first, second }));
        Assert.That(firstEntity.DomainEvents, Is.Empty);
        Assert.That(secondEntity.DomainEvents, Is.Empty);
    }

    [Test]
    public async Task CommitFailure_ShouldNotDispatchDomainEventsAgainstRealMySql()
    {
        var dispatcher = new CapturingDispatcher();
        await using var context = await MySqlIntegrationContextFactory.CreateAsync(_tenantId, dispatcher);
        await using var unit = new UnitOfWork<MySqlIntegrationDbContext>(context);
        var domainEvent = new MySqlTestDomainEvent(MySqlIntegrationConstants.FixedNow);
        var entity = new MySqlDomainEventEntity { TenantId = _tenantId, Name = "Commit failure" };
        entity.AddDomainEventForTest(domainEvent);
        context.DomainEventEntities.Add(entity);
        await unit.BeginTransactionAsync();
        await context.SaveChangesAsync();
        await context.Database.CurrentTransaction!.DisposeAsync();
        Assert.ThrowsAsync<ObjectDisposedException>(async () => await unit.CommitTransactionAsync());
        Assert.That(dispatcher.Events, Is.Empty);
        Assert.That(entity.DomainEvents, Is.Empty);
    }

    [Test]
    public async Task RollbackTransaction_ShouldNotDispatchDomainEventsAndShouldClearPendingStateAgainstRealMySql()
    {
        var dispatcher = new CapturingDispatcher();
        await using var context = await MySqlIntegrationContextFactory.CreateAsync(_tenantId, dispatcher);
        await using var unit = new UnitOfWork<MySqlIntegrationDbContext>(context);
        var entity = new MySqlDomainEventEntity { TenantId = _tenantId, Name = "Rollback event" };
        entity.AddDomainEventForTest(new MySqlTestDomainEvent(MySqlIntegrationConstants.FixedNow));
        context.DomainEventEntities.Add(entity);
        await unit.BeginTransactionAsync();
        await context.SaveChangesAsync();
        await unit.RollbackTransactionAsync();
        Assert.That(dispatcher.Events, Is.Empty);
        Assert.That(entity.DomainEvents, Is.Empty);
    }

    [Test]
    public async Task DispatcherFailureAfterCommit_ShouldLeaveDatabaseCommittedAndEventPendingAgainstRealMySql()
    {
        var dispatcher = new ThrowingDispatcher();
        await using var context = await MySqlIntegrationContextFactory.CreateAsync(_tenantId, dispatcher);
        await using var verification = await MySqlIntegrationContextFactory.CreateAsync(_tenantId);
        await using var unit = new UnitOfWork<MySqlIntegrationDbContext>(context);
        var domainEvent = new MySqlTestDomainEvent(MySqlIntegrationConstants.FixedNow);
        var entity = new MySqlDomainEventEntity { TenantId = _tenantId, Name = "Dispatcher failure" };
        entity.AddDomainEventForTest(domainEvent);
        context.DomainEventEntities.Add(entity);
        await unit.BeginTransactionAsync();
        Assert.ThrowsAsync<InvalidOperationException>(async () => await unit.CommitTransactionAsync());
        var persisted = await verification.DomainEventEntities.IgnoreQueryFilters().SingleAsync(x => x.Name == "Dispatcher failure");
        Assert.That(persisted.Id, Is.EqualTo(entity.Id));
        Assert.That(entity.DomainEvents, Contains.Item(domainEvent));
    }

    [Test]
    public async Task SlowQueryInterceptor_ScalarExecutedAsync_ShouldLogRealScalarQueryAgainstMySql()
    {
        int previousThreshold = SlowQueryInterceptor.SlowQueryThresholdMs;
        SlowQueryInterceptor.SlowQueryThresholdMs = 0;
        try
        {
            var logger = new MySqlCapturingLogger<SlowQueryInterceptor>();
            await using var context = await MySqlIntegrationContextFactory.CreateAsync(_tenantId, slowQueryInterceptor: new SlowQueryInterceptor(logger));
            await context.Database.OpenConnectionAsync();
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT 1";
            _ = await command.ExecuteScalarAsync();
            await context.Database.CloseConnectionAsync();
            Assert.That(logger.WarningMessages, Has.Some.Contains("[SlowQuery]"));
        }
        finally { SlowQueryInterceptor.SlowQueryThresholdMs = previousThreshold; }
    }

    [Test]
    public async Task SlowQueryInterceptor_ScalarExecuted_ShouldLogRealScalarQueryAgainstMySql()
    {
        int previousThreshold = SlowQueryInterceptor.SlowQueryThresholdMs;
        SlowQueryInterceptor.SlowQueryThresholdMs = 0;
        try
        {
            var logger = new MySqlCapturingLogger<SlowQueryInterceptor>();
            await using var context = await MySqlIntegrationContextFactory.CreateAsync(_tenantId, slowQueryInterceptor: new SlowQueryInterceptor(logger));
            await context.Database.OpenConnectionAsync();
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT 1";
            _ = command.ExecuteScalar();
            await context.Database.CloseConnectionAsync();
            Assert.That(logger.WarningMessages, Has.Some.Contains("[SlowQuery]"));
        }
        finally { SlowQueryInterceptor.SlowQueryThresholdMs = previousThreshold; }
    }

    private sealed class CapturingDispatcher : IDomainEventDispatcher
    {
        public List<IDomainEvent> Events { get; } = [];
        public Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default) { Events.Add(domainEvent); return Task.CompletedTask; }
    }

    private sealed class ThrowingDispatcher : IDomainEventDispatcher
    {
        public Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Dispatcher failure");
    }

    private sealed class MySqlCapturingLogger<T> : ILogger<T>
    {
        public List<string> WarningMessages { get; } = [];
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { if (logLevel == LogLevel.Warning) WarningMessages.Add(formatter(state, exception)); }
        private sealed class NullScope : IDisposable { public static readonly NullScope Instance = new(); public void Dispose() { } }
    }
}