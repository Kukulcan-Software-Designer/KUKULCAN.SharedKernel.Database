using KUKULCAN.SharedKernel.Database.Tests.TestInfrastructure;
using KUKULCAN.SharedKernel.Database.Tests.TestInfrastructure.internals;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace KUKULCAN.SharedKernel.Database.Tests.Coverage;

/// <summary>
/// Additional regression tests introduced by the coverage audit.
/// These tests target execution paths that are easy to miss when testing only
/// the primary persistence scenarios.
/// </summary>
[TestFixture]
public sealed class CoverageGapTests
{
    [Test]
    public void DbContext_WithoutSlowQueryInterceptor_ShouldRegisterBaseInterceptors()
    {
        using TestDbContext context = DatabaseTestContextFactory.Create().Context;

        CoreOptionsExtension coreOptions = context
            .GetService<IDbContextOptions>()
            .Extensions
            .OfType<CoreOptionsExtension>()
            .Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(coreOptions.Interceptors, Has.Some.TypeOf<AuditSaveChangesInterceptor>());
            Assert.That(coreOptions.Interceptors, Has.Some.TypeOf<SoftDeleteInterceptor>());
            Assert.That(coreOptions.Interceptors, Has.Some.TypeOf<DomainEventDispatchInterceptor>());
            Assert.That(coreOptions.Interceptors, Has.Some.TypeOf<ImmutableEntityInterceptor>());
            Assert.That(coreOptions.Interceptors, Has.None.TypeOf<SlowQueryInterceptor>());
        }
    }

    [Test]
    public void DbContext_WithDefaultLoggingOptions_ShouldNotEnableSensitiveLoggingOrDetailedErrors()
    {
        var result = DatabaseTestContextFactory.Create();
        using TestDbContext context = result.Context;

        CoreOptionsExtension coreOptions = context
            .GetService<IDbContextOptions>()
            .Extensions
            .OfType<CoreOptionsExtension>()
            .Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(coreOptions.IsSensitiveDataLoggingEnabled, Is.False);
            Assert.That(coreOptions.DetailedErrorsEnabled, Is.False);
        }
    }

    [Test]
    public async Task DomainEvents_SyncSave_WithNoEvents_ShouldNotDispatch()
    {
        var result = DatabaseTestContextFactory.Create();
        using TestDbContext context = result.Context;

        context.Add(new DomainEventEntityForTests());
        context.SaveChanges();

        result.Dispatcher.Verify(
            x => x.DispatchAsync(It.IsAny<IDomainEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);

        await Task.CompletedTask;
    }

    [Test]
    public async Task SoftDelete_WhenEntityIsNotDeleted_ShouldLeaveStateUntouched()
    {
        var result = DatabaseTestContextFactory.Create();
        await using TestDbContext context = result.Context;

        var entity = new SoftDeleteEntityForTests
        {
            IsDeleted = false,
            DeletedOn = null
        };

        context.Add(entity);
        await context.SaveChangesAsync();

        Assert.Multiple(() =>
        {
            Assert.That(entity.IsDeleted, Is.False);
            Assert.That(entity.DeletedOn, Is.Null);
        });
    }

    [Test]
    public async Task Audit_WhenEntityIsUnchanged_ShouldLeaveAuditFieldsUntouched()
    {
        var result = DatabaseTestContextFactory.Create();
        await using TestDbContext context = result.Context;

        var entity = new AuditableEntityForTests();
        context.Add(entity);
        await context.SaveChangesAsync();

        DateTimeOffset createdOn = entity.CreatedOn;
        DateTimeOffset? modifiedOn = entity.ModifiedOn;

        context.Entry(entity).State = EntityState.Unchanged;
        await context.SaveChangesAsync();

        Assert.Multiple(() =>
        {
            Assert.That(entity.CreatedOn, Is.EqualTo(createdOn));
            Assert.That(entity.ModifiedOn, Is.EqualTo(modifiedOn));
        });
    }

    [Test]
    public void ServiceRegistration_ShouldResolveSlowQueryInterceptorFromDependencyInjection()
    {
        var services = new ServiceCollection();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{KukulcanDatabaseOptions.SectionKey}:ConnectionString"] = "DataSource=test",
                [$"{KukulcanDatabaseOptions.SectionKey}:Provider"] = "SqlServer"
            })
            .Build();

        services.AddLogging();
        services.AddKukulcanDbContext<TestDbContext>(configuration);

        using ServiceProvider provider = services.BuildServiceProvider();
        SlowQueryInterceptor interceptor = provider.GetRequiredService<SlowQueryInterceptor>();

        Assert.That(interceptor, Is.Not.Null);
        Assert.That(
            provider.GetRequiredService<SlowQueryInterceptor>(),
            Is.SameAs(interceptor));
    }

    [Test]
    public void ServiceRegistration_ShouldExposeUnitOfWorkAsScoped()
    {
        var services = new ServiceCollection();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{KukulcanDatabaseOptions.SectionKey}:ConnectionString"] = "DataSource=test"
            })
            .Build();

        services.AddKukulcanDbContext<TestDbContext>(configuration);

        ServiceDescriptor descriptor = services.Single(x => x.ServiceType == typeof(IUnitOfWork));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(descriptor.Lifetime, Is.EqualTo(ServiceLifetime.Scoped));
            Assert.That(descriptor.ImplementationType, Is.EqualTo(typeof(UnitOfWork<TestDbContext>)));
        }
    }

    [Test]
    public void SlowQueryThreshold_Zero_ShouldTreatNormalCommandAsSlow()
    {
        List<string> messages = new();
        using ILoggerFactory factory = LoggerFactory.Create(builder =>
            builder.AddProvider(new ListLoggerProvider(messages)));

        ILogger<SlowQueryInterceptor> logger = new Logger<SlowQueryInterceptor>(factory);
        var interceptor = new SlowQueryInterceptor(
            logger,
            Options.Create(new KukulcanDatabaseOptions
            {
                EnableSensitiveDataLogging = false
            }));

        int previousThreshold = SlowQueryInterceptor.SlowQueryThresholdMs;
        try
        {
            SlowQueryInterceptor.SlowQueryThresholdMs = 0;

            using var context = new SqliteCoverageContext(interceptor);
            context.Database.OpenConnection();
            context.Database.ExecuteSqlRaw("SELECT 1");

            Assert.That(messages, Has.Some.Contains("[SlowQuery]"));
        }
        finally
        {
            SlowQueryInterceptor.SlowQueryThresholdMs = previousThreshold;
        }
    }

    private sealed class SqliteCoverageContext(SlowQueryInterceptor interceptor) : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder
                .UseSqlite("Data Source=:memory:")
                .AddInterceptors(interceptor);
    }

    private sealed class ListLoggerProvider(List<string> messages) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new ListLogger(messages);

        public void Dispose()
        {
        }
    }

    private sealed class ListLogger(List<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => messages.Add(formatter(state, exception));
    }
}
