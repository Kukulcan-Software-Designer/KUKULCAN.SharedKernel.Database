using KUKULCAN.SharedKernel.Database.Configuration;
using KUKULCAN.SharedKernel.Database.Interceptors;
using KUKULCAN.SharedKernel.Database.UnitOfWork;
using KUKULCAN.SharedKernel.DomainEvents.Abstractions;
using Microsoft.Extensions.Options;

namespace KUKULCAN.SharedKernel.Database.Integration;

[TestFixture]
[NonParallelizable]
public sealed class DomainEventsAndScalarMissingCoverageIntegrationTests
{
    private Guid _tenantId;

    [SetUp]
    public void SetUp() => _tenantId = Guid.NewGuid();

    private PostgreSqlDatabaseIntegrationTests.IntegrationDbContext Create(IDomainEventDispatcher dispatcher, SlowQueryInterceptor? interceptor = null)
        => new(
            Options.Create(new KukulcanDatabaseOptions
            {
                Provider = DatabaseProvider.PostgresSql,
                ConnectionString = IntegrationTestDatabase.ConnectionString,
                Retry = new KukulcanDatabaseOptions.RetryOptions { Enabled = false },
                Pool = new KukulcanDatabaseOptions.PoolOptions { Enabled = false }
            }),
            new PostgreSqlDatabaseIntegrationTests.IntegrationTenantContext(_tenantId),
            new PostgreSqlDatabaseIntegrationTests.FixedClock(PostgreSqlDatabaseIntegrationTests.FixedNow),
            dispatcher,
            interceptor);

    [Test]
    public async Task CommitTransaction_ShouldPersistAndDispatchDomainEventFromRealPostgreSql()
    {
        var dispatcher = new CapturingDispatcher();
        await using var context = Create(dispatcher);
        await using var verification = Create(new CapturingDispatcher());
        await context.Database.EnsureCreatedAsync();
        await verification.Database.EnsureCreatedAsync();
        await using var unit = new UnitOfWork<PostgreSqlDatabaseIntegrationTests.IntegrationDbContext>(context);
        var domainEvent = new PostgreSqlDatabaseIntegrationTests.TestDomainEvent(PostgreSqlDatabaseIntegrationTests.FixedNow);
        var entity = new PostgreSqlDatabaseIntegrationTests.DomainEventEntity { TenantId = _tenantId, Name = "Committed event" };
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
    public async Task CommitTransaction_ShouldDispatchMultipleAggregateEventsOnceAgainstRealPostgreSql()
    {
        var dispatcher = new CapturingDispatcher();
        await using var context = Create(dispatcher);
        await context.Database.EnsureCreatedAsync();
        await using var unit = new UnitOfWork<PostgreSqlDatabaseIntegrationTests.IntegrationDbContext>(context);
        var first = new PostgreSqlDatabaseIntegrationTests.TestDomainEvent(PostgreSqlDatabaseIntegrationTests.FixedNow);
        var second = new PostgreSqlDatabaseIntegrationTests.TestDomainEvent(PostgreSqlDatabaseIntegrationTests.FixedNow.AddSeconds(1));
        var firstEntity = new PostgreSqlDatabaseIntegrationTests.DomainEventEntity { TenantId = _tenantId, Name = "Aggregate A" };
        var secondEntity = new PostgreSqlDatabaseIntegrationTests.DomainEventEntity { TenantId = _tenantId, Name = "Aggregate B" };
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
    public async Task RollbackTransaction_ShouldNotDispatchDomainEventsAndShouldClearPendingStateAgainstRealPostgreSql()
    {
        var dispatcher = new CapturingDispatcher();
        await using var context = Create(dispatcher);
        await context.Database.EnsureCreatedAsync();
        await using var unit = new UnitOfWork<PostgreSqlDatabaseIntegrationTests.IntegrationDbContext>(context);
        var entity = new PostgreSqlDatabaseIntegrationTests.DomainEventEntity { TenantId = _tenantId, Name = "Rollback event" };
        entity.AddDomainEventForTest(new PostgreSqlDatabaseIntegrationTests.TestDomainEvent(PostgreSqlDatabaseIntegrationTests.FixedNow));
        context.DomainEventEntities.Add(entity);
        await unit.BeginTransactionAsync();
        await context.SaveChangesAsync();
        await unit.RollbackTransactionAsync();
        Assert.That(dispatcher.Events, Is.Empty);
        Assert.That(entity.DomainEvents, Is.Empty);
    }

    [Test]
    public async Task DispatcherFailureAfterCommit_ShouldLeaveDatabaseCommittedAndEventPendingAgainstRealPostgreSql()
    {
        var dispatcher = new ThrowingDispatcher();
        await using var context = Create(dispatcher);
        await using var verification = Create(new CapturingDispatcher());
        await context.Database.EnsureCreatedAsync();
        await verification.Database.EnsureCreatedAsync();
        await using var unit = new UnitOfWork<PostgreSqlDatabaseIntegrationTests.IntegrationDbContext>(context);
        var domainEvent = new PostgreSqlDatabaseIntegrationTests.TestDomainEvent(PostgreSqlDatabaseIntegrationTests.FixedNow);
        var entity = new PostgreSqlDatabaseIntegrationTests.DomainEventEntity { TenantId = _tenantId, Name = "Dispatcher failure" };
        entity.AddDomainEventForTest(domainEvent);
        context.DomainEventEntities.Add(entity);
        await unit.BeginTransactionAsync();
        Assert.ThrowsAsync<InvalidOperationException>(async () => await unit.CommitTransactionAsync());
        var persisted = await verification.DomainEventEntities.IgnoreQueryFilters().SingleAsync(x => x.Name == "Dispatcher failure");
        Assert.That(persisted.Id, Is.EqualTo(entity.Id));
        Assert.That(entity.DomainEvents, Contains.Item(domainEvent));
    }

    [Test]
    public async Task SlowQueryInterceptor_ScalarExecutedAsync_ShouldLogRealScalarQueryAgainstPostgreSql()
    {
        int previousThreshold = SlowQueryInterceptor.SlowQueryThresholdMs;
        SlowQueryInterceptor.SlowQueryThresholdMs = 0;
        try
        {
            var logger = new PostgreSqlCapturingLogger<SlowQueryInterceptor>();
            await using var context = Create(new CapturingDispatcher(), new SlowQueryInterceptor(logger));
            await context.Database.EnsureCreatedAsync();
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
    public async Task SlowQueryInterceptor_ScalarExecuted_ShouldLogRealScalarQueryAgainstPostgreSql()
    {
        int previousThreshold = SlowQueryInterceptor.SlowQueryThresholdMs;
        SlowQueryInterceptor.SlowQueryThresholdMs = 0;
        try
        {
            var logger = new PostgreSqlCapturingLogger<SlowQueryInterceptor>();
            await using var context = Create(new CapturingDispatcher(), new SlowQueryInterceptor(logger));
            await context.Database.EnsureCreatedAsync();
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

    private sealed class PostgreSqlCapturingLogger<T> : ILogger<T>
    {
        public List<string> WarningMessages { get; } = [];
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { if (logLevel == LogLevel.Warning) WarningMessages.Add(formatter(state, exception)); }
        private sealed class NullScope : IDisposable { public static readonly NullScope Instance = new(); public void Dispose() { } }
    }
}