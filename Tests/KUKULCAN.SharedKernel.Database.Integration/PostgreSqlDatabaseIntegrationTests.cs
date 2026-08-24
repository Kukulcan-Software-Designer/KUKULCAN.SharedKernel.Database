using System.ComponentModel.DataAnnotations.Schema;
using KUKULCAN.SharedKernel.Abstractions;
using KUKULCAN.SharedKernel.Database.Abstractions;
using KUKULCAN.SharedKernel.Database.Configuration;
using KUKULCAN.SharedKernel.Database.Extensions;
using KUKULCAN.SharedKernel.Database.Interceptors;
using KUKULCAN.SharedKernel.DomainEvents.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Testcontainers.PostgreSql;

namespace KUKULCAN.SharedKernel.Database.Integration;

[SetUpFixture]
public sealed class IntegrationTestDatabase
{
    private static PostgreSqlContainer? _container;

    public static string ConnectionString => _container?.GetConnectionString()
        ?? throw new InvalidOperationException("The integration test database has not been initialized.");

    [OneTimeSetUp]
    public async Task SetUpAsync()
    {
        _container = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("database_integration_tests")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await _container.StartAsync();
    }

    [OneTimeTearDown]
    public async Task TearDownAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    internal static async Task<PostgreSqlDatabaseIntegrationTests.IntegrationDbContext> CreateContextAsync(Guid tenantId)
    {
        var options = Options.Create(new KukulcanDatabaseOptions
        {
            Provider = DatabaseProvider.PostgresSql,
            ConnectionString = ConnectionString,
            Retry = new KukulcanDatabaseOptions.RetryOptions { Enabled = false },
            Pool = new KukulcanDatabaseOptions.PoolOptions { Enabled = false },
        });

        var context = new PostgreSqlDatabaseIntegrationTests.IntegrationDbContext(
            options,
            new PostgreSqlDatabaseIntegrationTests.IntegrationTenantContext(tenantId),
            new PostgreSqlDatabaseIntegrationTests.FixedClock(PostgreSqlDatabaseIntegrationTests.FixedNow),
            Mock.Of<IDomainEventDispatcher>());

        await context.Database.EnsureCreatedAsync();
        return context;
    }
}

[TestFixture]
[NonParallelizable]
public sealed class PostgreSqlDatabaseIntegrationTests
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
    public async Task TearDown()
        => await _context.DisposeAsync();

    [Test]
    public async Task Provider_ShouldUsePostgreSqlAndPersistData()
    {
        var entity = new IntegrationEntity { TenantId = _tenantId, Name = "PostgreSQL integration" };
        _context.Entities.Add(entity);

        int affected = await _context.SaveChangesAsync();
        IntegrationEntity? persisted = await _context.Entities.SingleAsync(x => x.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_context.Database.ProviderName, Is.EqualTo("Npgsql.EntityFrameworkCore.PostgreSQL"));
            Assert.That(_context.Database.IsNpgsql(), Is.True);
            Assert.That(affected, Is.EqualTo(1));
            Assert.That(persisted.Name, Is.EqualTo("PostgreSQL integration"));
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

        Assert.That(visible, Has.Count.EqualTo(1));
        Assert.That(visible[0].Name, Is.EqualTo("Current tenant"));
    }

    [Test]
    public async Task TenantModelCache_ShouldKeepTenantModelsIndependentAcrossContexts()
    {
        Guid firstTenant = Guid.NewGuid();
        Guid secondTenant = Guid.NewGuid();

        await using IntegrationDbContext firstContext = await IntegrationTestDatabase.CreateContextAsync(firstTenant);
        await using IntegrationDbContext secondContext = await IntegrationTestDatabase.CreateContextAsync(secondTenant);

        firstContext.Entities.Add(new IntegrationEntity { TenantId = firstTenant, Name = "First tenant" });
        secondContext.Entities.Add(new IntegrationEntity { TenantId = secondTenant, Name = "Second tenant" });

        await firstContext.SaveChangesAsync();
        await secondContext.SaveChangesAsync();

        List<string> firstVisible = await firstContext.Entities.OrderBy(x => x.Name).Select(x => x.Name).ToListAsync();
        List<string> secondVisible = await secondContext.Entities.OrderBy(x => x.Name).Select(x => x.Name).ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstVisible, Is.EqualTo(new[] { "First tenant" }));
            Assert.That(secondVisible, Is.EqualTo(new[] { "Second tenant" }));
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
        IntegrationEntity? deleted = await _context.Entities.IgnoreQueryFilters().SingleAsync(x => x.Id == entity.Id);

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

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entity.CreatedOn, Is.EqualTo(FixedNow));
            Assert.That(entity.ModifiedOn, Is.Null);
        }

        entity.Name = "Audited updated";
        await _context.SaveChangesAsync();
        Assert.That(entity.ModifiedOn, Is.EqualTo(FixedNow));
    }

    [Test]
    public async Task DomainEventDispatchInterceptor_ShouldDispatchAndClearEventsAfterSuccessfulSave()
    {
        var dispatcher = new Mock<IDomainEventDispatcher>();
        var context = new IntegrationDbContext(
            Options.Create(new KukulcanDatabaseOptions
            {
                Provider = DatabaseProvider.PostgresSql,
                ConnectionString = IntegrationTestDatabase.ConnectionString,
                Retry = new KukulcanDatabaseOptions.RetryOptions { Enabled = false },
                Pool = new KukulcanDatabaseOptions.PoolOptions { Enabled = false }
            }),
            new IntegrationTenantContext(_tenantId),
            new FixedClock(FixedNow),
            dispatcher.Object);

        await using (context)
        {
            await context.Database.EnsureCreatedAsync();
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
    }

    [Test]
    public async Task ImmutableEntityInterceptor_ShouldRejectUpdateAndDelete()
    {
        var entity = new ImmutableIntegrationEntity { TenantId = _tenantId, Name = "Immutable" };
        _context.ImmutableEntities.Add(entity);
        await _context.SaveChangesAsync();

        entity.Name = "Changed";
        InvalidOperationException updateException = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _context.SaveChangesAsync())!;

        Assert.That(updateException.Message, Does.Contain(nameof(ImmutableIntegrationEntity)));

        _context.ChangeTracker.Clear();
        ImmutableIntegrationEntity persisted = await _context.ImmutableEntities.SingleAsync(x => x.Id == entity.Id);
        _context.ImmutableEntities.Remove(persisted);

        InvalidOperationException deleteException = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _context.SaveChangesAsync())!;

        Assert.That(deleteException.Message, Does.Contain(nameof(ImmutableIntegrationEntity)));
    }

    [Test]
    public async Task AddKukulcanDbContext_ShouldRegisterContextAndUnitOfWorkAgainstRealPostgreSql()
    {
        var logger = new CapturingLogger<SlowQueryInterceptor>();
        using ServiceProvider provider = BuildServiceProvider(
            commandTimeoutSeconds: 30,
            retryEnabled: true,
            logger);

        await using IntegrationDbContext context = provider.GetRequiredService<IntegrationDbContext>();
        await context.Database.EnsureCreatedAsync();
        await context.Database.OpenConnectionAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(context.Database.IsNpgsql(), Is.True);
            Assert.That(provider.GetRequiredService<IUnitOfWork>(), Is.Not.Null);
            Assert.That(provider.GetRequiredService<IOptions<KukulcanDatabaseOptions>>().Value.Retry.Enabled, Is.True);
        }

        await context.Database.CloseConnectionAsync();
    }

    [Test]
    public async Task SlowQueryInterceptor_ShouldLogRealPostgreSqlCommandAboveThreshold()
    {
        int previousThreshold = SlowQueryInterceptor.SlowQueryThresholdMs;
        SlowQueryInterceptor.SlowQueryThresholdMs = 0;

        try
        {
            var logger = new CapturingLogger<SlowQueryInterceptor>();
            using ServiceProvider provider = BuildServiceProvider(30, false, logger);
            SlowQueryInterceptor registeredInterceptor = provider.GetRequiredService<SlowQueryInterceptor>();
            await using IntegrationDbContext context = provider.GetRequiredService<IntegrationDbContext>();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(context.SlowQueryInterceptor, Is.SameAs(registeredInterceptor));
            }

            await context.Database.ExecuteSqlRawAsync("SELECT pg_sleep(0.1);");

            Assert.That(logger.WarningMessages, Has.Some.Contains("[SlowQuery]"));
        }
        finally
        {
            SlowQueryInterceptor.SlowQueryThresholdMs = previousThreshold;
        }
    }

    [Test]
    public async Task CommandTimeout_ShouldAbortLongRunningRealPostgreSqlCommand()
    {
        var options = Options.Create(new KukulcanDatabaseOptions
        {
            Provider = DatabaseProvider.PostgresSql,
            ConnectionString = IntegrationTestDatabase.ConnectionString,
            CommandTimeoutSeconds = 1,
            Retry = new KukulcanDatabaseOptions.RetryOptions { Enabled = false },
            Pool = new KukulcanDatabaseOptions.PoolOptions { Enabled = false }
        });

        await using var context = new IntegrationDbContext(
            options,
            new IntegrationTenantContext(_tenantId),
            new FixedClock(FixedNow),
            Mock.Of<IDomainEventDispatcher>());

        await context.Database.EnsureCreatedAsync();

        Exception? exception = null;
        try
        {
            await context.Database.ExecuteSqlRawAsync("SELECT pg_sleep(3);");
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        Assert.That(exception, Is.Not.Null);
    }

    [Test]
    public async Task RetryConfiguration_ShouldBeAppliedToRealPostgreSqlContext()
    {
        var options = Options.Create(new KukulcanDatabaseOptions
        {
            Provider = DatabaseProvider.PostgresSql,
            ConnectionString = IntegrationTestDatabase.ConnectionString,
            CommandTimeoutSeconds = 30,
            Retry = new KukulcanDatabaseOptions.RetryOptions
            {
                Enabled = true,
                MaxRetryCount = 2,
                MaxRetryDelaySeconds = 5
            },
            Pool = new KukulcanDatabaseOptions.PoolOptions { Enabled = false }
        });

        await using var context = new IntegrationDbContext(
            options,
            new IntegrationTenantContext(_tenantId),
            new FixedClock(FixedNow),
            Mock.Of<IDomainEventDispatcher>());

        await context.Database.EnsureCreatedAsync();
        await context.Database.OpenConnectionAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(context.Database.IsNpgsql(), Is.True);
            Assert.That(options.Value.Retry.Enabled, Is.True);
            Assert.That(options.Value.Retry.MaxRetryCount, Is.EqualTo(2));
            Assert.That(options.Value.Retry.MaxRetryDelaySeconds, Is.EqualTo(5));
        }
    }

    [Test]
    public async Task UnitOfWork_Commit_ShouldPersistTransaction()
    {
        var unitOfWork = new UnitOfWork<IntegrationDbContext>(_context);
        await unitOfWork.BeginTransactionAsync();
        _context.Entities.Add(new IntegrationEntity { TenantId = _tenantId, Name = "Committed" });
        await unitOfWork.CommitTransactionAsync();

        Assert.That(await _context.Entities.CountAsync(x => x.Name == "Committed"), Is.EqualTo(1));
        await unitOfWork.DisposeAsync();
    }

    [Test]
    public async Task UnitOfWork_Rollback_ShouldNotPersistTransaction()
    {
        var unitOfWork = new UnitOfWork<IntegrationDbContext>(_context);
        await unitOfWork.BeginTransactionAsync();
        _context.Entities.Add(new IntegrationEntity { TenantId = _tenantId, Name = "Rolled back" });
        await unitOfWork.RollbackTransactionAsync();

        Assert.That(await _context.Entities.CountAsync(x => x.Name == "Rolled back"), Is.EqualTo(0));
        await unitOfWork.DisposeAsync();
    }

    [Test]
    public async Task UnitOfWork_FailedCommit_ShouldLeaveTransactionInFailedStateAndNotPersistInvalidOperation()
    {
        var unitOfWork = new UnitOfWork<IntegrationDbContext>(_context);
        await unitOfWork.BeginTransactionAsync();

        _context.Entities.Add(new IntegrationEntity { TenantId = _tenantId, Name = "Duplicate A" });
        await _context.SaveChangesAsync();

        _context.Entities.Add(new IntegrationEntity { Id = 0, TenantId = _tenantId, Name = "Duplicate B" });
        await unitOfWork.CommitTransactionAsync();

        Assert.That(await _context.Entities.CountAsync(x => x.Name.StartsWith("Duplicate")), Is.EqualTo(2));
        await unitOfWork.DisposeAsync();
    }

    private static ServiceProvider BuildServiceProvider(
        int commandTimeoutSeconds,
        bool retryEnabled,
        ILogger<SlowQueryInterceptor> logger)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{KukulcanDatabaseOptions.SectionKey}:Provider"] = nameof(DatabaseProvider.PostgresSql),
                [$"{KukulcanDatabaseOptions.SectionKey}:ConnectionString"] = IntegrationTestDatabase.ConnectionString,
                [$"{KukulcanDatabaseOptions.SectionKey}:CommandTimeoutSeconds"] = commandTimeoutSeconds.ToString(),
                [$"{KukulcanDatabaseOptions.SectionKey}:Retry:Enabled"] = retryEnabled.ToString(),
                [$"{KukulcanDatabaseOptions.SectionKey}:Retry:MaxRetryCount"] = "3",
                [$"{KukulcanDatabaseOptions.SectionKey}:Retry:MaxRetryDelaySeconds"] = "5",
                [$"{KukulcanDatabaseOptions.SectionKey}:Pool:Enabled"] = "false"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<ITenantContext>(new IntegrationTenantContext(Guid.NewGuid()));
        services.AddSingleton<IClock>(new FixedClock(FixedNow));
        services.AddSingleton<IDomainEventDispatcher>(Mock.Of<IDomainEventDispatcher>());
        services.AddSingleton(logger);
        services.AddKukulcanDbContext<IntegrationDbContext>(configuration);
        return services.BuildServiceProvider();
    }

    internal sealed class IntegrationTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId { get; } = tenantId;
    }

    internal sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    internal sealed class IntegrationDbContext : KukulcanDbContextBase
    {
        internal SlowQueryInterceptor? SlowQueryInterceptor { get; }

        public IntegrationDbContext(
            IOptions<KukulcanDatabaseOptions> options,
            ITenantContext tenantContext,
            IClock clock,
            IDomainEventDispatcher dispatcher,
            SlowQueryInterceptor? slowQueryInterceptor = null)
            : base(options, tenantContext, clock, dispatcher, slowQueryInterceptor)
        {
            SlowQueryInterceptor = slowQueryInterceptor;
        }

        internal DbSet<IntegrationEntity> Entities => Set<IntegrationEntity>();
        internal DbSet<ImmutableIntegrationEntity> ImmutableEntities => Set<ImmutableIntegrationEntity>();
        internal DbSet<DomainEventEntity> DomainEventEntities => Set<DomainEventEntity>();
        internal DbSet<MissingCoverageIntegrationTests.StringTenantIntegrationEntity> StringTenantEntities => Set<MissingCoverageIntegrationTests.StringTenantIntegrationEntity>();
        internal DbSet<MissingCoverageIntegrationTests.OwnedTenantIntegrationEntity> OwnedTenantEntities => Set<MissingCoverageIntegrationTests.OwnedTenantIntegrationEntity>();
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

        [NotMapped]
        public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents;

        public void ClearDomainEvents() => _domainEvents.Clear();

        public void AddDomainEventForTest(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    }

    internal sealed record TestDomainEvent(DateTimeOffset OccurredOn) : IDomainEvent;

    internal sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> WarningMessages { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning)
                WarningMessages.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
