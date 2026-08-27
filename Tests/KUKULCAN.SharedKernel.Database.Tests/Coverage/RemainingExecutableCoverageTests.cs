using System.Reflection;
using System.Runtime.Loader;
using KUKULCAN.SharedKernel.Database.Tests.TestInfrastructure.internals;

namespace KUKULCAN.SharedKernel.Database.Tests.Coverage;

[TestFixture]
public sealed class RemainingExecutableCoverageTests
{
    [Test]
    public void TenantModelCacheKeyFactory_WithNonKukulcanContext_ShouldUseNullTenant()
    {
        Type factoryType = typeof(KukulcanDbContextBase).Assembly
            .GetType("KUKULCAN.SharedKernel.Database.TenantModelCacheKeyFactory", throwOnError: true)!;

        object factory = Activator.CreateInstance(factoryType)!;
        using var context = new PlainDbContext();

        MethodInfo createMethod = factoryType.GetMethod("Create")!;
        object keyObject = createMethod.Invoke(factory, [context, false])!;
        var key = ((Type ContextType, Guid? TenantId, bool DesignTime))keyObject;

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
                [null, "ignored", 30, 0, TimeSpan.FromSeconds(5)]))!;

        Assert.That(exception.InnerException, Is.TypeOf<NotSupportedException>());
        Assert.That(exception.InnerException!.Message, Does.Contain("Failed to configure provider"));
    }

    [Test]
    public void ConfigurePostgresSql_WhenProviderThrows_ShouldWrapFailure()
    {
        MethodInfo method = typeof(KukulcanDbContextBase).GetMethod(
            "ConfigurePostgresSql", BindingFlags.Static | BindingFlags.NonPublic)!;

        TargetInvocationException exception = Assert.Throws<TargetInvocationException>(() =>
            method.Invoke(null,
                [null, "ignored", 30, 0, TimeSpan.FromSeconds(5)]))!;

        Assert.That(exception.InnerException, Is.TypeOf<NotSupportedException>());
        Assert.That(exception.InnerException!.Message, Does.Contain("Failed to configure provider"));
    }

    [Test]
    public void LoadProviderExtensionType_WhenExactTypeIsMissing_ShouldFindTypeByShortName()
    {
        MethodInfo method = typeof(KukulcanDbContextBase).GetMethod(
            "FindProviderExtensionType", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assembly assembly = Assembly.Load(new AssemblyName("Microsoft.EntityFrameworkCore.SqlServer"));
        Type expectedType = assembly.GetType(
            "Microsoft.EntityFrameworkCore.SqlServerDbContextOptionsExtensions", throwOnError: true)!;

        object result = method.Invoke(null,
            [assembly, "Some.Unrelated.Namespace.SqlServerDbContextOptionsExtensions", assembly.GetName().Name!])!;

        Assert.That(result, Is.EqualTo(expectedType));
    }

    [Test]
    public void LoadProviderExtensionType_WhenTypeCannotBeFound_ShouldThrowNotSupportedException()
    {
        MethodInfo method = typeof(KukulcanDbContextBase).GetMethod(
            "FindProviderExtensionType", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assembly assembly = Assembly.Load(new AssemblyName("Microsoft.EntityFrameworkCore.SqlServer"));

        TargetInvocationException exception = Assert.Throws<TargetInvocationException>(() =>
            method.Invoke(null,
                [assembly, "Some.Unrelated.Namespace.TypeThatDoesNotExist", assembly.GetName().Name!]))!;

        Assert.That(exception.InnerException, Is.TypeOf<NotSupportedException>());
        Assert.That(exception.InnerException!.Message, Does.Contain("does not expose the expected provider extension type"));
    }

    [Test]
    public void LoadProviderExtensionType_WhenAssemblyTypesPartiallyFail_ShouldUseLoadableTypes()
    {
        MethodInfo method = typeof(KukulcanDbContextBase).GetMethod(
            "FindProviderExtensionType", BindingFlags.Static | BindingFlags.NonPublic)!;
        string assemblyPath = typeof(PartiallyLoadableProvider.SqlServerDbContextOptionsExtensions).Assembly.Location;
        var loadContext = new AssemblyLoadContext("Coverage.PartiallyLoadableProvider", isCollectible: true);
        Assembly assembly = loadContext.LoadFromAssemblyPath(assemblyPath);

        try
        {
            TargetInvocationException exception = Assert.Throws<TargetInvocationException>(() =>
                method.Invoke(null,
                    [assembly, "PartiallyLoadableProvider.BrokenProviderType", assembly.GetName().Name!]))!;

            Assert.That(exception.InnerException, Is.TypeOf<NotSupportedException>());
            Assert.That(exception.InnerException!.Message, Does.Contain("does not expose the expected provider extension type"));
        }
        finally
        {
            loadContext.Unload();
        }
    }

    [Test]
    public void AppendConnectionStringOptions_WithEmptyConnectionString_ShouldReturnOptions()
    {
        MethodInfo method = typeof(KukulcanDbContextBase).GetMethod(
            "AppendConnectionStringOptions", BindingFlags.Static | BindingFlags.NonPublic)!;

        const string options = "Pooling=true;Min Pool Size=1;Max Pool Size=10";
        object result = method.Invoke(null, [string.Empty, options])!;

        Assert.That(result, Is.EqualTo(options));
    }

    [Test]
    public void RemoveConnectionStringKeys_WithEmptyConnectionString_ShouldReturnEmpty()
    {
        MethodInfo method = typeof(KukulcanDbContextBase).GetMethod(
            "RemoveConnectionStringKeys", BindingFlags.Static | BindingFlags.NonPublic)!;

        object result = method.Invoke(null, [string.Empty, new[] { "Pooling" }])!;

        Assert.That(result, Is.EqualTo(string.Empty));
    }

    [Test]
    public void InvokeProviderUseMethod_WhenNoCompatibleMethodExists_ShouldThrow()
    {
        MethodInfo method = typeof(KukulcanDbContextBase).GetMethod(
            "InvokeProviderUseMethod", BindingFlags.Static | BindingFlags.NonPublic)!;

        TargetInvocationException exception = Assert.Throws<TargetInvocationException>(() =>
            method.Invoke(null,
                [typeof(string), "UseMissing", new DbContextOptionsBuilder(), "ignored", 30, 0, TimeSpan.FromSeconds(5), null]))!;

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
                [typeof(FakeProviderExtensions), "UseFake", new DbContextOptionsBuilder(), "ignored", 3, 2, TimeSpan.FromSeconds(5), null]))!;

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
