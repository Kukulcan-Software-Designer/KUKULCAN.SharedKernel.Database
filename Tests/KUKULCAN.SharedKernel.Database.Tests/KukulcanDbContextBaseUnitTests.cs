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

    private static IOptions<KukulcanDatabaseOptions> CreateOptions()
        => Options.Create(new KukulcanDatabaseOptions
        {
            Provider = DatabaseProvider.PostgresSql,
            ConnectionString = "Host=unit-test;Database=unit-test;Username=unit-test;Password=unit-test"
        });

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
