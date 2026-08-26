using System.Reflection;
using KUKULCAN.SharedKernel.Database.Tests.TestInfrastructure.internals;

namespace KUKULCAN.SharedKernel.Database.Tests.Coverage;

[TestFixture]
public sealed class FullLineCoverageTests
{
    [Test]
    public void BuildProviderConnectionString_WithUnsupportedProvider_ShouldReturnOriginalConnectionString()
    {
        MethodInfo method = typeof(KukulcanDbContextBase).GetMethod(
            "BuildProviderConnectionString", BindingFlags.Static | BindingFlags.NonPublic)!;

        const string original = "Server=localhost;Database=KukulcanTests;";
        var pool = new KukulcanDatabaseOptions.PoolOptions
        {
            Enabled = true,
            MinSize = 3,
            MaxSize = 9
        };

        string result = (string)method.Invoke(null, [(DatabaseProvider)999, original, pool])!;

        Assert.That(result, Is.EqualTo(original));
    }

    [Test]
    public void RemoveConnectionStringKeys_WithMalformedSegment_ShouldPreserveSegment()
    {
        MethodInfo method = typeof(KukulcanDbContextBase).GetMethod(
            "RemoveConnectionStringKeys", BindingFlags.Static | BindingFlags.NonPublic)!;

        string result = (string)method.Invoke(null,
            ["MalformedSegment;Pooling=true;Database=KukulcanTests;", new[] { "Pooling" }])!;

        Assert.That(result, Is.EqualTo("MalformedSegment;Database=KukulcanTests"));
    }

    [Test]
    public void LoadProviderExtensionType_WithMissingAssembly_ShouldReportPackageNotInstalled()
    {
        MethodInfo method = typeof(KukulcanDbContextBase).GetMethod(
            "LoadProviderExtensionType", BindingFlags.Static | BindingFlags.NonPublic)!;

        TargetInvocationException exception = Assert.Throws<TargetInvocationException>(() =>
            method.Invoke(null,
                ["Missing.Provider.Extensions", "KUKULCAN.SharedKernel.Database.Tests.Missing.Provider.Assembly"]))!;

        Assert.That(exception.InnerException, Is.TypeOf<NotSupportedException>());
        Assert.That(exception.InnerException!.Message, Does.Contain("Failed to configure provider"));
        Assert.That(exception.InnerException.InnerException, Is.TypeOf<FileNotFoundException>());
    }

    [Test]
    public void SlowQueryInterceptor_ScalarExecuted_ShouldLogSlowQuery()
    {
        List<string> messages = [];
        using ILoggerFactory factory = LoggerFactory.Create(builder =>
            builder.AddProvider(new ListLoggerProvider(messages)));

        var interceptor = new SlowQueryInterceptor(
            new Logger<SlowQueryInterceptor>(factory),
            Options.Create(new KukulcanDatabaseOptions { EnableSensitiveDataLogging = true }));

        int previousThreshold = SlowQueryInterceptor.SlowQueryThresholdMs;
        SlowQueryInterceptor.SlowQueryThresholdMs = -1;

        try
        {
            using var context = new ScalarTestDbContext(interceptor);
            context.Database.OpenConnection();
            context.Database.EnsureCreated();
            context.Rows.Add(new ScalarTestRow());
            context.SaveChanges();

            int count = context.Rows.Count();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(count, Is.EqualTo(1));
                Assert.That(messages, Has.Some.Contains("COUNT("));
            }
        }
        finally
        {
            SlowQueryInterceptor.SlowQueryThresholdMs = previousThreshold;
        }
    }

    private sealed class ScalarTestDbContext(SlowQueryInterceptor interceptor) : DbContext
    {
        public DbSet<ScalarTestRow> Rows => Set<ScalarTestRow>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder
                .UseSqlite("Data Source=:memory:")
                .AddInterceptors(interceptor);
    }

    private sealed class ScalarTestRow
    {
        public int Id { get; set; }
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
