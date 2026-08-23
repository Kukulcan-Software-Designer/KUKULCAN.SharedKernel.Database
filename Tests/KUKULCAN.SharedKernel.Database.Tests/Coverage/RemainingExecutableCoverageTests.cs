using System.Reflection;
using KUKULCAN.SharedKernel.Database.Tests.TestInfrastructure.internals;

namespace KUKULCAN.SharedKernel.Database.Tests.Coverage;

[TestFixture]
public sealed class RemainingExecutableCoverageTests
{
    [Test]
    public void TenantModelCacheKeyFactory_WithNonKukulcanContext_ShouldUseNullTenant()
    {
        var factory = new TenantModelCacheKeyFactory();
        using var context = new PlainDbContext();

        (Type ContextType, Guid? TenantId, bool DesignTime) key =
            ((Type, Guid?, bool))factory.Create(context, designTime: false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(key.ContextType, Is.EqualTo(typeof(PlainDbContext)));
            Assert.That(key.TenantId, Is.Null);
            Assert.That(key.DesignTime, Is.False);
        }
    }

    [Test]
    public void SoftDeleteInterceptor_WithNullContext_ShouldBeIgnored()
    {
        var interceptor = new SoftDeleteInterceptor(new TestClock(DateTimeOffset.UtcNow));
        MethodInfo method = typeof(SoftDeleteInterceptor).GetMethod(
            "ConvertDeletes", BindingFlags.Instance | BindingFlags.NonPublic)!;

        Assert.DoesNotThrow(() => method.Invoke(interceptor, [null]));
    }

    [Test]
    public void ImmutableEntityInterceptor_WithNullContext_ShouldBeIgnored()
    {
        MethodInfo method = typeof(ImmutableEntityInterceptor).GetMethod(
            "ThrowIfImmutableEntityModified", BindingFlags.Static | BindingFlags.NonPublic)!;

        Assert.DoesNotThrow(() => method.Invoke(null, [null]));
    }

    [Test]
    public void AuditSaveChangesInterceptor_WithNullContext_ShouldBeIgnored()
    {
        var interceptor = new AuditSaveChangesInterceptor(new TestClock(DateTimeOffset.UtcNow));
        MethodInfo method = typeof(AuditSaveChangesInterceptor).GetMethod(
            "UpdateAuditFields", BindingFlags.Instance | BindingFlags.NonPublic)!;

        Assert.DoesNotThrow(() => method.Invoke(interceptor, [null]));
    }

    [Test]
    public void ConfigureSqlServer_WhenProviderThrows_ShouldWrapFailure()
    {
        MethodInfo method = typeof(KukulcanDbContextBase).GetMethod(
            "ConfigureSqlServer", BindingFlags.Static | BindingFlags.NonPublic)!;

        TargetInvocationException exception = Assert.Throws<TargetInvocationException>(() =>
            method.Invoke(null,
                [new DbContextOptionsBuilder(), null!, 30, 0, TimeSpan.FromSeconds(5)]))!;

        Assert.That(exception.InnerException, Is.TypeOf<NotSupportedException>());
        Assert.That(exception.InnerException!.Message, Does.Contain("Microsoft.EntityFrameworkCore.SqlServer"));
    }

    [Test]
    public void ConfigurePostgresSql_WhenProviderThrows_ShouldWrapFailure()
    {
        MethodInfo method = typeof(KukulcanDbContextBase).GetMethod(
            "ConfigurePostgresSql", BindingFlags.Static | BindingFlags.NonPublic)!;

        TargetInvocationException exception = Assert.Throws<TargetInvocationException>(() =>
            method.Invoke(null,
                [new DbContextOptionsBuilder(), null!, 30, 0, TimeSpan.FromSeconds(5)]))!;

        Assert.That(exception.InnerException, Is.TypeOf<NotSupportedException>());
        Assert.That(exception.InnerException!.Message, Does.Contain("Npgsql.EntityFrameworkCore.PostgreSQL"));
    }

    [Test]
    public void InvokeProviderUseMethod_WhenNoCompatibleMethodExists_ShouldThrow()
    {
        MethodInfo method = typeof(KukulcanDbContextBase).GetMethod(
            "InvokeProviderUseMethod", BindingFlags.Static | BindingFlags.NonPublic)!;

        TargetInvocationException exception = Assert.Throws<TargetInvocationException>(() =>
            method.Invoke(null,
                [typeof(string), "UseMissing", new DbContextOptionsBuilder(), "ignored", 30, 0, TimeSpan.FromSeconds(5)]))!;

        Assert.That(exception.InnerException, Is.TypeOf<NotSupportedException>());
        Assert.That(exception.InnerException!.Message, Does.Contain("UseMissing"));
    }

    [Test]
    public void InvokeProviderUseMethod_WithProviderWithoutRetrySupport_ShouldThrow()
    {
        MethodInfo method = typeof(KukulcanDbContextBase).GetMethod(
            "InvokeProviderUseMethod", BindingFlags.Static | BindingFlags.NonPublic)!;

        TargetInvocationException outer = Assert.Throws<TargetInvocationException>(() =>
            method.Invoke(null,
                [typeof(FakeProviderExtensions), "UseFake", new DbContextOptionsBuilder(), "ignored", 3, 2, TimeSpan.FromSeconds(5)]))!;

        Exception? inner = outer.InnerException;
        while (inner is TargetInvocationException invocation && invocation.InnerException is not null)
            inner = invocation.InnerException;

        Assert.That(inner, Is.TypeOf<NotSupportedException>());
        Assert.That(inner!.Message, Does.Contain("EnableRetryOnFailure"));
    }

    [Test]
    public async Task SlowQueryInterceptor_ReaderExecutedAsync_ShouldLogSlowQuery()
    {
        List<string> messages = [];
        using ILoggerFactory factory = LoggerFactory.Create(builder =>
            builder.AddProvider(new ListLoggerProvider(messages)));
        var interceptor = new SlowQueryInterceptor(
            new Logger<SlowQueryInterceptor>(factory),
            Options.Create(new KukulcanDatabaseOptions { EnableSensitiveDataLogging = true }));

        SlowQueryInterceptor.SlowQueryThresholdMs = -1;

        await using var context = new ReaderTestDbContext(interceptor);
        await context.Database.OpenConnectionAsync();
        await context.Database.EnsureCreatedAsync();

        List<int> values = await context.Database
            .SqlQueryRaw<int>("SELECT 321 AS Value")
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(values, Is.EqualTo([321]));
            Assert.That(messages, Has.Some.Contains("SELECT 321"));
        }
    }

    [Test]
    public void SlowQueryInterceptor_ReaderExecuted_ShouldLogSlowQuery()
    {
        List<string> messages = [];
        using ILoggerFactory factory = LoggerFactory.Create(builder =>
            builder.AddProvider(new ListLoggerProvider(messages)));
        var interceptor = new SlowQueryInterceptor(
            new Logger<SlowQueryInterceptor>(factory),
            Options.Create(new KukulcanDatabaseOptions { EnableSensitiveDataLogging = true }));

        SlowQueryInterceptor.SlowQueryThresholdMs = -1;

        using var context = new ReaderTestDbContext(interceptor);
        context.Database.OpenConnection();
        context.Database.EnsureCreated();

        List<int> values = context.Database
            .SqlQueryRaw<int>("SELECT 654 AS Value")
            .ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(values, Is.EqualTo([654]));
            Assert.That(messages, Has.Some.Contains("SELECT 654"));
        }
    }

    private sealed class PlainDbContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseInMemoryDatabase(Guid.NewGuid().ToString());
    }

    private sealed class ReaderTestDbContext(SlowQueryInterceptor interceptor) : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder
                .UseSqlite("Data Source=:memory:")
                .AddInterceptors(interceptor);
    }

    public sealed class FakeProviderOptions
    {
    }

    public static class FakeProviderExtensions
    {
        public static DbContextOptionsBuilder UseFake(
            DbContextOptionsBuilder optionsBuilder,
            string connectionString,
            Action<FakeProviderOptions> configure)
        {
            configure(new FakeProviderOptions());
            return optionsBuilder;
        }
    }

    private sealed class ListLoggerProvider(List<string> messages) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new ListLogger(messages);
        public void Dispose() { }
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
