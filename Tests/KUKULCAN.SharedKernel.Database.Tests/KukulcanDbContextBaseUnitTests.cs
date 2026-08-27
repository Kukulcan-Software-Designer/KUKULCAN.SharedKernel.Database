using System.Reflection;
using KUKULCAN.SharedKernel.Database.Configuration;
using NUnit.Framework;

namespace KUKULCAN.SharedKernel.Database.Tests;

[TestFixture]
public sealed class KukulcanDbContextBaseUnitTests
{
    [Test]
    public void Constructor_ShouldRejectNullOptions()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
            new UnitTestDbContext(
                null,
                Mock.Of<ITenantContext>(),
                Mock.Of<IClock>(),
                Mock.Of<IDomainEventDispatcher>()))!;

        Assert.That(exception.ParamName, Is.EqualTo("options"));
    }

    [Test]
    public void Constructor_ShouldRejectOptionsWithNullValue()
    {
        var options = new Mock<IOptions<KukulcanDatabaseOptions>>();
        options.SetupGet(x => x.Value).Returns((KukulcanDatabaseOptions)null!);

        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
            new UnitTestDbContext(
                options.Object,
                Mock.Of<ITenantContext>(),
                Mock.Of<IClock>(),
                Mock.Of<IDomainEventDispatcher>()))!;

        Assert.That(exception.ParamName, Is.EqualTo("options"));
    }

    [Test]
    public void Constructor_ShouldRejectNullTenantContext()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
            new UnitTestDbContext(
                CreateOptions(),
                null!,
                Mock.Of<IClock>(),
                Mock.Of<IDomainEventDispatcher>()))!;

        Assert.That(exception.ParamName, Is.EqualTo("tenantContext"));
    }

    [Test]
    public void Constructor_ShouldRejectNullClock()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
            new UnitTestDbContext(
                CreateOptions(),
                Mock.Of<ITenantContext>(),
                null!,
                Mock.Of<IDomainEventDispatcher>()))!;

        Assert.That(exception.ParamName, Is.EqualTo("clock"));
    }

    [Test]
    public void Constructor_ShouldRejectNullDomainEventDispatcher()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
            new UnitTestDbContext(
                CreateOptions(),
                Mock.Of<ITenantContext>(),
                Mock.Of<IClock>(),
                null!))!;

        Assert.That(exception.ParamName, Is.EqualTo("domainEventDispatcher"));
    }

    [Test]
    public void ConfigureProvider_ShouldRejectUnsupportedProvider()
    {
        var options = CreateOptions();
        options.Value.Provider = (DatabaseProvider)999;

        using var context = new UnitTestDbContext(
            options,
            Mock.Of<ITenantContext>(),
            Mock.Of<IClock>(),
            Mock.Of<IDomainEventDispatcher>());

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            context.ConfigureProviderForTest(new DbContextOptionsBuilder()))!;

        Assert.That(exception.Message, Does.Contain("999"));
        Assert.That(exception.Message, Does.Contain("not supported"));
    }

    [Test]
    public void ConfigureProvider_WithRetryDisabled_ShouldNotEnableProviderRetry()
    {
        var options = Options.Create(new KukulcanDatabaseOptions
        {
            Provider = DatabaseProvider.PostgresSql,
            ConnectionString = "Host=localhost;Database=KukulcanTests;Username=test;Password=test",
            CommandTimeoutSeconds = 31,
            Retry = new KukulcanDatabaseOptions.RetryOptions
            {
                Enabled = false,
                MaxRetryCount = 7,
                MaxRetryDelaySeconds = 12
            }
        });

        using var context = new UnitTestDbContext(
            options,
            Mock.Of<ITenantContext>(),
            Mock.Of<IClock>(),
            Mock.Of<IDomainEventDispatcher>());

        var builder = new DbContextOptionsBuilder();
        context.ConfigureProviderForTest(builder);

        using var configured = new DbContext(builder.Options);
        Assert.That(configured.Database.ProviderName, Is.EqualTo("Npgsql.EntityFrameworkCore.PostgreSQL"));
        Assert.That(configured.Database.GetCommandTimeout(), Is.EqualTo(31));
        Assert.That(configured.Database.CreateExecutionStrategy().GetType().Name,
            Is.EqualTo("NpgsqlExecutionStrategy"));
    }

    [TestCase(DatabaseProvider.SqlServer,
        "Server=localhost;Database=Coverage;Pooling=true;Min Pool Size=2;Max Pool Size=9;")]
    [TestCase(DatabaseProvider.PostgresSql,
        "Server=localhost;Database=Coverage;Pooling=true;Minimum Pool Size=2;Maximum Pool Size=9;")]
    [TestCase(DatabaseProvider.MySql,
        "Server=localhost;Database=Coverage;Pooling=true;MinimumPoolSize=2;MaximumPoolSize=9;")]
    public void BuildProviderConnectionString_WithPoolingEnabled_ShouldAppendProviderSpecificKeys(
        DatabaseProvider provider,
        string expected)
    {
        var pool = new KukulcanDatabaseOptions.PoolOptions
        {
            Enabled = true,
            MinSize = 2,
            MaxSize = 9
        };

        string result = InvokePrivate<string>(
            "BuildProviderConnectionString",
            provider,
            "Server=localhost;Database=Coverage;",
            pool);

        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase(DatabaseProvider.SqlServer, "Pooling;Min Pool Size;Max Pool Size")]
    [TestCase(DatabaseProvider.PostgresSql, "Pooling;Minimum Pool Size;Maximum Pool Size")]
    [TestCase(DatabaseProvider.MySql, "Pooling;MinimumPoolSize;MaximumPoolSize;MinPoolSize;MaxPoolSize")]
    public void BuildProviderConnectionString_WithPoolingDisabled_ShouldRemoveProviderSpecificKeys(
        DatabaseProvider provider,
        string keys)
    {
        var pool = new KukulcanDatabaseOptions.PoolOptions { Enabled = false };
        string connectionString =
            "Server=localhost;Database=Coverage;Pooling=true;Min Pool Size=2;Max Pool Size=9;" +
            "Minimum Pool Size=3;Maximum Pool Size=8;MinimumPoolSize=4;MaximumPoolSize=7;" +
            "MinPoolSize=5;MaxPoolSize=6;Application Name=Coverage;";

        string result = InvokePrivate<string>(
            "BuildProviderConnectionString",
            provider,
            connectionString,
            pool);

        Assert.That(result, Does.Contain("Application Name=Coverage"));
        foreach (string key in keys.Split(';'))
            Assert.That(result, Does.Not.Contain(key + "="));
    }

    [Test]
    public void BuildProviderConnectionString_WithEmptyConnectionString_ShouldReturnOnlyPoolOptions()
    {
        var pool = new KukulcanDatabaseOptions.PoolOptions
        {
            Enabled = true,
            MinSize = 1,
            MaxSize = 4
        };

        string result = InvokePrivate<string>(
            "BuildProviderConnectionString",
            DatabaseProvider.PostgresSql,
            string.Empty,
            pool);

        Assert.That(result, Is.EqualTo("Pooling=true;Minimum Pool Size=1;Maximum Pool Size=4"));
    }

    [Test]
    public void BuildProviderConnectionString_WithWhitespaceAndDisabledPooling_ShouldPreserveWhitespaceInput()
    {
        var pool = new KukulcanDatabaseOptions.PoolOptions { Enabled = false };

        string result = InvokePrivate<string>(
            "BuildProviderConnectionString",
            DatabaseProvider.SqlServer,
            "   ",
            pool);

        Assert.That(result, Is.EqualTo("   "));
    }

    [Test]
    public void BuildProviderConnectionString_WithUnsupportedProvider_ShouldLeaveConnectionStringUnchanged()
    {
        var pool = new KukulcanDatabaseOptions.PoolOptions { Enabled = true };
        const string connectionString = "Server=localhost;Database=Coverage;";

        string result = InvokePrivate<string>(
            "BuildProviderConnectionString",
            (DatabaseProvider)999,
            connectionString,
            pool);

        Assert.That(result, Is.EqualTo(connectionString));
    }

    [Test]
    public void BuildProviderConnectionString_WithUnsupportedProviderAndPoolingDisabled_ShouldLeaveConnectionStringUnchanged()
    {
        var pool = new KukulcanDatabaseOptions.PoolOptions { Enabled = false };
        const string connectionString = "Server=localhost;Database=Coverage;";

        string result = InvokePrivate<string>(
            "BuildProviderConnectionString",
            (DatabaseProvider)999,
            connectionString,
            pool);

        Assert.That(result, Is.EqualTo(connectionString));
    }

    [Test]
    public void AppendConnectionStringOptions_WithWhitespaceConnectionString_ShouldReturnOptionsOnly()
    {
        string result = InvokePrivate<string>(
            "AppendConnectionStringOptions",
            "  ",
            "Pooling=true");

        Assert.That(result, Is.EqualTo("Pooling=true"));
    }

    [Test]
    public void RemoveConnectionStringKeys_WithEmptyConnectionString_ShouldReturnOriginalValue()
    {
        string result = InvokePrivate<string>(
            "RemoveConnectionStringKeys",
            "",
            new[] { "Pooling" });

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void RemoveConnectionStringKeys_WithSegmentsWithoutEquals_ShouldPreserveThemAndMatchKeysCaseInsensitively()
    {
        string result = InvokePrivate<string>(
            "RemoveConnectionStringKeys",
            "Server=localhost;POOLING=true;MalformedSegment;Database=Coverage;",
            new[] { "Pooling" });

        Assert.That(result, Is.EqualTo("Server=localhost;MalformedSegment;Database=Coverage"));
    }

    [Test]
    public void FindProviderExtensionType_WithFullyQualifiedTypeMissing_ShouldUseShortNameFallback()
    {
        Assembly assembly = typeof(KukulcanDbContextBaseUnitTests).Assembly;

        Type result = InvokePrivate<Type>(
            "FindProviderExtensionType",
            assembly,
            "Missing.Namespace.UnitTestDbContext",
            "TestAssembly");

        Assert.That(result, Is.EqualTo(typeof(UnitTestDbContext)));
    }

    [Test]
    public void FindProviderExtensionType_WhenExpectedTypeDoesNotExist_ShouldThrowNotSupportedException()
    {
        Assembly assembly = typeof(KukulcanDbContextBaseUnitTests).Assembly;

        TargetInvocationException exception = Assert.Throws<TargetInvocationException>(() =>
            InvokePrivate<Type>(
                "FindProviderExtensionType",
                assembly,
                "Missing.Namespace.DoesNotExist",
                "TestAssembly"))!;

        Assert.That(exception.InnerException, Is.TypeOf<NotSupportedException>());
        Assert.That(exception.InnerException!.Message,
            Does.Contain("does not expose the expected provider extension type"));
    }

    private static IOptions<KukulcanDatabaseOptions> CreateOptions()
        => Options.Create(new KukulcanDatabaseOptions
        {
            Provider = DatabaseProvider.PostgresSql,
            ConnectionString = "Host=unit-test;Database=unit-test;Username=unit-test;Password=unit-test"
        });

    private static T InvokePrivate<T>(string methodName, params object?[] arguments)
    {
        MethodInfo method = typeof(KukulcanDbContextBase).GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Static)!;

        return (T)method.Invoke(null, arguments)!;
    }

    private sealed class UnitTestDbContext : KukulcanDbContextBase
    {
        public UnitTestDbContext(
            IOptions<KukulcanDatabaseOptions>? options,
            ITenantContext tenantContext,
            IClock clock,
            IDomainEventDispatcher domainEventDispatcher)
            : base(options, tenantContext, clock, domainEventDispatcher)
        {
        }

        public void ConfigureProviderForTest(DbContextOptionsBuilder optionsBuilder)
            => ConfigureProvider(optionsBuilder);
    }
}
