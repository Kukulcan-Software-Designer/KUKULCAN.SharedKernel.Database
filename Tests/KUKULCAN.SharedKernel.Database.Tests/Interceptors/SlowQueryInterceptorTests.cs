namespace KUKULCAN.SharedKernel.Database.Tests.Interceptors;

[TestFixture]
public sealed class SlowQueryInterceptorTests
{
    [SetUp]
    public void SetUp() => SlowQueryInterceptor.SlowQueryThresholdMs = 500;

    [TearDown]
    public void TearDown() => SlowQueryInterceptor.SlowQueryThresholdMs = 500;

    [Test]
    public void Threshold_ShouldBeMutable()
    {
        SlowQueryInterceptor.SlowQueryThresholdMs = 1000;

        Assert.That(SlowQueryInterceptor.SlowQueryThresholdMs, Is.EqualTo(1000));
    }

    [Test]
    public void Constructor_ShouldCreateInterceptor()
    {
        using ILoggerFactory factory = LoggerFactory.Create(builder => { });
        var logger = new Logger<SlowQueryInterceptor>(factory);
        var interceptor = new SlowQueryInterceptor(
            logger,
            Options.Create(new KukulcanDatabaseOptions()));

        Assert.That(interceptor, Is.Not.Null);
    }

    [Test]
    public async Task ReaderExecuted_WhenDurationExceedsThreshold_ShouldLogWarning()
    {
        var messages = new List<string>();
        using ILoggerFactory factory = LoggerFactory.Create(builder =>
            builder.AddProvider(new ListLoggerProvider(messages)));

        var logger = new Logger<SlowQueryInterceptor>(factory);
        var interceptor = new SlowQueryInterceptor(
            logger,
            Options.Create(new KukulcanDatabaseOptions { EnableSensitiveDataLogging = false }));

        SlowQueryInterceptor.SlowQueryThresholdMs = -1;

        await using var context = new SqliteTestContext(interceptor);
        await context.Database.OpenConnectionAsync();
        await context.Database.EnsureCreatedAsync();
        await context.Database.ExecuteSqlRawAsync("SELECT 1");

        Assert.That(messages, Has.Some.Contains("[SlowQuery]"));
        Assert.That(messages, Has.Some.Contains("[SQL hidden"));
    }

    [Test]
    public async Task ReaderExecuted_WhenSensitiveLoggingEnabled_ShouldIncludeSql()
    {
        var messages = new List<string>();
        using ILoggerFactory factory = LoggerFactory.Create(builder =>
            builder.AddProvider(new ListLoggerProvider(messages)));

        var logger = new Logger<SlowQueryInterceptor>(factory);
        var interceptor = new SlowQueryInterceptor(
            logger,
            Options.Create(new KukulcanDatabaseOptions { EnableSensitiveDataLogging = true }));

        SlowQueryInterceptor.SlowQueryThresholdMs = -1;

        await using var context = new SqliteTestContext(interceptor);
        await context.Database.OpenConnectionAsync();
        await context.Database.EnsureCreatedAsync();
        await context.Database.ExecuteSqlRawAsync("SELECT 42");

        Assert.That(messages, Has.Some.Contains("SELECT 42"));
    }



    [Test]
    public void ReaderExecuted_SyncPath_WhenDurationExceedsThreshold_ShouldLogWarning()
    {
        var messages = new List<string>();
        using ILoggerFactory factory = LoggerFactory.Create(builder =>
            builder.AddProvider(new ListLoggerProvider(messages)));

        var logger = new Logger<SlowQueryInterceptor>(factory);
        var interceptor = new SlowQueryInterceptor(
            logger,
            Options.Create(new KukulcanDatabaseOptions { EnableSensitiveDataLogging = true }));

        SlowQueryInterceptor.SlowQueryThresholdMs = -1;

        using var context = new SqliteTestContext(interceptor);
        context.Database.OpenConnection();
        context.Database.EnsureCreated();
        context.Database.ExecuteSqlRaw("SELECT 99");

        Assert.That(messages, Has.Some.Contains("SELECT 99"));
    }

    private sealed class SqliteTestContext(SlowQueryInterceptor interceptor) : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder
                .UseSqlite("Data Source=:memory:")
                .AddInterceptors(interceptor);
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
