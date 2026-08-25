using System.Reflection;
using Testcontainers.MySql;

namespace KUKULCAN.SharedKernel.Database.Integration.MySQL;

[SetUpFixture]
public sealed class MySqlIntegrationDatabase
{
    private static MySqlContainer? _container;

    public static string ConnectionString => _container?.GetConnectionString()
        ?? throw new InvalidOperationException("MySQL integration container is not initialized.");

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _container = new MySqlBuilder("mysql:8.4")
            .WithDatabase("kukulcan_test")
            .WithUsername("root")
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

internal static class MySqlIntegrationContextFactory
{
    public static async Task<MySqlIntegrationDbContext> CreateAsync(Guid tenantId, IDomainEventDispatcher? dispatcher = null, SlowQueryInterceptor? slowQueryInterceptor = null)
    {
        var options = Options.Create(new KukulcanDatabaseOptions
        {
            Provider = DatabaseProvider.MySql,
            ConnectionString = MySqlIntegrationDatabase.ConnectionString,
            CommandTimeoutSeconds = 30,
            Retry = new KukulcanDatabaseOptions.RetryOptions { Enabled = false },
            Pool = new KukulcanDatabaseOptions.PoolOptions { Enabled = false },
        });

        var context = new MySqlIntegrationDbContext(
            options,
            new MySqlTenantContext(tenantId),
            new FixedClock(MySqlIntegrationConstants.FixedNow),
            dispatcher ?? Mock.Of<IDomainEventDispatcher>(),
            slowQueryInterceptor);

        await context.Database.EnsureCreatedAsync();
        return context;
    }
}

internal static class MySqlTenantModelCacheKeyHelper
{
    private static readonly Type FactoryType = typeof(KukulcanDbContextBase).Assembly
        .GetType("KUKULCAN.SharedKernel.Database.TenantModelCacheKeyFactory", throwOnError: true)!;

    private static readonly ConstructorInfo Constructor = FactoryType
        .GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, [], null, [], null)!;

    private static readonly MethodInfo CreateMethod = FactoryType
        .GetMethod(nameof(IModelCacheKeyFactory.Create), BindingFlags.Instance | BindingFlags.Public, [typeof(DbContext), typeof(bool)])!;

    public static object Create(DbContext context, bool designTime)
    {
        object factory = Constructor.Invoke([]);
        return CreateMethod.Invoke(factory, [context, designTime])!;
    }
}

internal static class MySqlIntegrationConstants
{
    public static readonly DateTimeOffset FixedNow = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
}

internal sealed class MySqlTenantContext(Guid tenantId) : ITenantContext
{
    public Guid TenantId { get; } = tenantId;
}

internal sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; } = now;
}

internal sealed class MySqlIntegrationDbContext(
    IOptions<KukulcanDatabaseOptions> options,
    ITenantContext tenantContext,
    IClock clock,
    IDomainEventDispatcher dispatcher,
    SlowQueryInterceptor? slowQueryInterceptor = null)
    : KukulcanDbContextBase(options, tenantContext, clock, dispatcher, slowQueryInterceptor)
{
    public DbSet<MySqlIntegrationEntity> Entities => Set<MySqlIntegrationEntity>();
    public DbSet<MySqlImmutableEntity> ImmutableEntities => Set<MySqlImmutableEntity>();
    public DbSet<MySqlDomainEventEntity> DomainEventEntities => Set<MySqlDomainEventEntity>();
}

internal sealed class MySqlIntegrationEntity : IAuditable, ISoftDelete
{
    public int Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedOn { get; set; }
    public DateTimeOffset? ModifiedOn { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedOn { get; set; }
}

internal sealed class MySqlImmutableEntity : IImmutable
{
    public int Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
}

internal sealed class MySqlDomainEventEntity : IHasDomainEvents
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public int Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents;

    public void AddDomainEventForTest(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
}

internal sealed class MySqlConfiguredIntegrationEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

internal sealed class MySqlConfiguredIntegrationEntityConfiguration : IEntityTypeConfiguration<MySqlConfiguredIntegrationEntity>
{
    public void Configure(EntityTypeBuilder<MySqlConfiguredIntegrationEntity> builder)
    {
        builder.ToTable("ConfiguredIntegrationEntities");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
    }
}

internal sealed record MySqlTestDomainEvent(DateTimeOffset OccurredOn) : IDomainEvent;

internal sealed class MySqlCapturingLogger<T> : ILogger<T>
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
