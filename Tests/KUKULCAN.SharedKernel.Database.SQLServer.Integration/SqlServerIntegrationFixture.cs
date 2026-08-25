using Testcontainers.MsSql;

namespace KUKULCAN.SharedKernel.Database.SQLServer.Integration;

[SetUpFixture]
public sealed class SqlServerIntegrationDatabase
{
    private static MsSqlContainer? _container;

    public static string ConnectionString => _container?.GetConnectionString()
        ?? throw new InvalidOperationException("SQL Server integration container is not initialized.");

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04")
            .WithPassword("Kukulcan1!")
            .Build();
        await _container.StartAsync();
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}

internal static class SqlServerIntegrationContextFactory
{
    public static async Task<SqlServerIntegrationDbContext> CreateAsync(Guid tenantId, IDomainEventDispatcher? dispatcher = null, SlowQueryInterceptor? slowQueryInterceptor = null)
    {
        var options = Options.Create(new KukulcanDatabaseOptions
        {
            Provider = DatabaseProvider.SqlServer,
            ConnectionString = SqlServerIntegrationDatabase.ConnectionString,
            CommandTimeoutSeconds = 30,
            Retry = new KukulcanDatabaseOptions.RetryOptions { Enabled = false },
            Pool = new KukulcanDatabaseOptions.PoolOptions { Enabled = false },
        });

        var context = new SqlServerIntegrationDbContext(
            options,
            new SqlServerTenantContext(tenantId),
            new FixedClock(SqlServerIntegrationConstants.FixedNow),
            dispatcher ?? Mock.Of<IDomainEventDispatcher>(),
            slowQueryInterceptor);

        await context.Database.EnsureCreatedAsync();
        return context;
    }
}

internal static class SqlServerIntegrationConstants
{
    public static readonly DateTimeOffset FixedNow = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
}

internal sealed class SqlServerTenantContext(Guid tenantId) : ITenantContext
{
    public Guid TenantId { get; } = tenantId;
}

internal sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; } = now;
}

internal sealed class SqlServerIntegrationDbContext(
    IOptions<KukulcanDatabaseOptions> options,
    ITenantContext tenantContext,
    IClock clock,
    IDomainEventDispatcher dispatcher,
    SlowQueryInterceptor? slowQueryInterceptor = null)
    : KukulcanDbContextBase(options, tenantContext, clock, dispatcher, slowQueryInterceptor)
{
    public DbSet<SqlServerIntegrationEntity> Entities => Set<SqlServerIntegrationEntity>();
    public DbSet<SqlServerImmutableEntity> ImmutableEntities => Set<SqlServerImmutableEntity>();
    public DbSet<SqlServerDomainEventEntity> DomainEventEntities => Set<SqlServerDomainEventEntity>();
}

internal sealed class SqlServerIntegrationEntity : IAuditable, ISoftDelete
{
    public int Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedOn { get; set; }
    public DateTimeOffset? ModifiedOn { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedOn { get; set; }
}

internal sealed class SqlServerImmutableEntity : IImmutable
{
    public int Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
}

internal sealed class SqlServerDomainEventEntity : IHasDomainEvents
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public int Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents;

    public void AddDomainEventForTest(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
}

internal sealed class SqlServerConfiguredIntegrationEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

internal sealed class SqlServerConfiguredIntegrationEntityConfiguration : IEntityTypeConfiguration<SqlServerConfiguredIntegrationEntity>
{
    public void Configure(EntityTypeBuilder<SqlServerConfiguredIntegrationEntity> builder)
    {
        builder.ToTable("ConfiguredIntegrationEntities");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
    }
}

internal sealed record SqlServerTestDomainEvent(DateTimeOffset OccurredOn) : IDomainEvent;

internal sealed class SqlServerCapturingLogger<T> : ILogger<T>
{
    public List<string> WarningMessages { get; } = [];
    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (logLevel == LogLevel.Warning)
            WarningMessages.Add(formatter(state, exception));
    }

    private sealed class NullScope : IDisposable
    {
        internal static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
