using KUKULCAN.SharedKernel.Abstractions;
using KUKULCAN.SharedKernel.Database.Abstractions;
using KUKULCAN.SharedKernel.Database.Configuration;
using KUKULCAN.SharedKernel.Database.Extensions;
using KUKULCAN.SharedKernel.Database.Interceptors;
using KUKULCAN.SharedKernel.Database.UnitOfWork;
using KUKULCAN.SharedKernel.DomainEvents.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

namespace KUKULCAN.SharedKernel.Database.Integration;

[TestFixture]
[NonParallelizable]
public sealed class ServiceCollectionRegistrationAdditionalIntegrationTests
{
    [Test]
    public void AddKukulcanDbContext_ShouldRejectMissingConnectionString()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{KukulcanDatabaseOptions.SectionKey}:Provider"] = nameof(DatabaseProvider.PostgresSql)
            })
            .Build();

        var services = new ServiceCollection();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => services.AddKukulcanDbContext<PostgreSqlDatabaseIntegrationTests.IntegrationDbContext>(configuration))!;

        Assert.That(exception.Message, Does.Contain("ConnectionString"));
    }

    [Test]
    public void AddKukulcanDbContext_ShouldBindDatabaseOptionsFromConfiguration()
    {
        IConfiguration configuration = CreateConfiguration(17, retryEnabled: true, poolEnabled: false);
        var services = new ServiceCollection();

        services.AddKukulcanDbContext<PostgreSqlDatabaseIntegrationTests.IntegrationDbContext>(configuration);

        using ServiceProvider provider = services.BuildServiceProvider();
        KukulcanDatabaseOptions options = provider
            .GetRequiredService<IOptions<KukulcanDatabaseOptions>>()
            .Value;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(options.Provider, Is.EqualTo(DatabaseProvider.PostgresSql));
            Assert.That(options.ConnectionString, Is.EqualTo(IntegrationTestDatabase.ConnectionString));
            Assert.That(options.CommandTimeoutSeconds, Is.EqualTo(17));
            Assert.That(options.Retry.Enabled, Is.True);
            Assert.That(options.Pool.Enabled, Is.False);
        }
    }

    [Test]
    public void AddKukulcanDbContext_ShouldBindAllNestedDatabaseOptions()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{KukulcanDatabaseOptions.SectionKey}:Provider"] = nameof(DatabaseProvider.PostgresSql),
                [$"{KukulcanDatabaseOptions.SectionKey}:ConnectionString"] = IntegrationTestDatabase.ConnectionString,
                [$"{KukulcanDatabaseOptions.SectionKey}:CommandTimeoutSeconds"] = "41",
                [$"{KukulcanDatabaseOptions.SectionKey}:EnableSensitiveDataLogging"] = "true",
                [$"{KukulcanDatabaseOptions.SectionKey}:EnableDetailedErrors"] = "true",
                [$"{KukulcanDatabaseOptions.SectionKey}:Retry:Enabled"] = "true",
                [$"{KukulcanDatabaseOptions.SectionKey}:Retry:MaxRetryCount"] = "7",
                [$"{KukulcanDatabaseOptions.SectionKey}:Retry:MaxRetryDelaySeconds"] = "19",
                [$"{KukulcanDatabaseOptions.SectionKey}:Pool:Enabled"] = "true",
                [$"{KukulcanDatabaseOptions.SectionKey}:Pool:MinSize"] = "2",
                [$"{KukulcanDatabaseOptions.SectionKey}:Pool:MaxSize"] = "37",
                [$"{KukulcanDatabaseOptions.SectionKey}:Migration:AutoMigrateOnStartup"] = "true",
                [$"{KukulcanDatabaseOptions.SectionKey}:Migration:SeedDataOnStartup"] = "false"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddKukulcanDbContext<PostgreSqlDatabaseIntegrationTests.IntegrationDbContext>(configuration);

        using ServiceProvider provider = services.BuildServiceProvider();
        KukulcanDatabaseOptions options = provider
            .GetRequiredService<IOptions<KukulcanDatabaseOptions>>()
            .Value;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(options.Provider, Is.EqualTo(DatabaseProvider.PostgresSql));
            Assert.That(options.CommandTimeoutSeconds, Is.EqualTo(41));
            Assert.That(options.EnableSensitiveDataLogging, Is.True);
            Assert.That(options.EnableDetailedErrors, Is.True);
            Assert.That(options.Retry.Enabled, Is.True);
            Assert.That(options.Retry.MaxRetryCount, Is.EqualTo(7));
            Assert.That(options.Retry.MaxRetryDelaySeconds, Is.EqualTo(19));
            Assert.That(options.Pool.Enabled, Is.True);
            Assert.That(options.Pool.MinSize, Is.EqualTo(2));
            Assert.That(options.Pool.MaxSize, Is.EqualTo(37));
            Assert.That(options.Migration.AutoMigrateOnStartup, Is.True);
            Assert.That(options.Migration.SeedDataOnStartup, Is.False);
        }
    }

    [Test]
    public void AddKukulcanDbContext_ShouldRegisterInfrastructureWithExpectedLifetimes()
    {
        IConfiguration configuration = CreateConfiguration(30, retryEnabled: false, poolEnabled: false);
        var services = new ServiceCollection();

        services.AddKukulcanDbContext<PostgreSqlDatabaseIntegrationTests.IntegrationDbContext>(configuration);

        ServiceDescriptor contextDescriptor = services.Single(
            descriptor => descriptor.ServiceType == typeof(PostgreSqlDatabaseIntegrationTests.IntegrationDbContext));
        ServiceDescriptor unitOfWorkDescriptor = services.Single(
            descriptor => descriptor.ServiceType == typeof(IUnitOfWork));
        ServiceDescriptor interceptorDescriptor = services.Single(
            descriptor => descriptor.ServiceType == typeof(SlowQueryInterceptor));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(contextDescriptor.Lifetime, Is.EqualTo(ServiceLifetime.Scoped));
            Assert.That(unitOfWorkDescriptor.Lifetime, Is.EqualTo(ServiceLifetime.Scoped));
            Assert.That(unitOfWorkDescriptor.ImplementationType,
                Is.EqualTo(typeof(UnitOfWork<PostgreSqlDatabaseIntegrationTests.IntegrationDbContext>)));
            Assert.That(interceptorDescriptor.Lifetime, Is.EqualTo(ServiceLifetime.Singleton));
        }
    }

    [Test]
    public void AddKukulcanDbContext_ShouldResolveOneContextPerScope()
    {
        IConfiguration configuration = CreateConfiguration(30, retryEnabled: false, poolEnabled: false);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(new PostgreSqlDatabaseIntegrationTests.IntegrationTenantContext(Guid.NewGuid()));
        services.AddSingleton<IClock>(new PostgreSqlDatabaseIntegrationTests.FixedClock(PostgreSqlDatabaseIntegrationTests.FixedNow));
        services.AddSingleton<IDomainEventDispatcher>(Mock.Of<IDomainEventDispatcher>());
        services.AddKukulcanDbContext<PostgreSqlDatabaseIntegrationTests.IntegrationDbContext>(configuration);

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope firstScope = provider.CreateScope();
        using IServiceScope secondScope = provider.CreateScope();

        PostgreSqlDatabaseIntegrationTests.IntegrationDbContext first =
            firstScope.ServiceProvider.GetRequiredService<PostgreSqlDatabaseIntegrationTests.IntegrationDbContext>();
        PostgreSqlDatabaseIntegrationTests.IntegrationDbContext firstAgain =
            firstScope.ServiceProvider.GetRequiredService<PostgreSqlDatabaseIntegrationTests.IntegrationDbContext>();
        PostgreSqlDatabaseIntegrationTests.IntegrationDbContext second =
            secondScope.ServiceProvider.GetRequiredService<PostgreSqlDatabaseIntegrationTests.IntegrationDbContext>();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.SameAs(firstAgain));
            Assert.That(second, Is.Not.SameAs(first));
        }
    }

    [Test]
    public void AddKukulcanDbContext_ShouldRegisterUnitOfWorkAsScopedService()
    {
        IConfiguration configuration = CreateConfiguration(30, retryEnabled: false, poolEnabled: false);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(new PostgreSqlDatabaseIntegrationTests.IntegrationTenantContext(Guid.NewGuid()));
        services.AddSingleton<IClock>(new PostgreSqlDatabaseIntegrationTests.FixedClock(PostgreSqlDatabaseIntegrationTests.FixedNow));
        services.AddSingleton<IDomainEventDispatcher>(Mock.Of<IDomainEventDispatcher>());
        services.AddKukulcanDbContext<PostgreSqlDatabaseIntegrationTests.IntegrationDbContext>(configuration);

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope firstScope = provider.CreateScope();
        using IServiceScope secondScope = provider.CreateScope();

        IUnitOfWork first = firstScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        IUnitOfWork firstAgain = firstScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        IUnitOfWork second = secondScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.SameAs(firstAgain));
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(first, Is.TypeOf<UnitOfWork<PostgreSqlDatabaseIntegrationTests.IntegrationDbContext>>());
        }
    }

    private static IConfiguration CreateConfiguration(int commandTimeoutSeconds, bool retryEnabled, bool poolEnabled)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{KukulcanDatabaseOptions.SectionKey}:Provider"] = nameof(DatabaseProvider.PostgresSql),
                [$"{KukulcanDatabaseOptions.SectionKey}:ConnectionString"] = IntegrationTestDatabase.ConnectionString,
                [$"{KukulcanDatabaseOptions.SectionKey}:CommandTimeoutSeconds"] = commandTimeoutSeconds.ToString(),
                [$"{KukulcanDatabaseOptions.SectionKey}:Retry:Enabled"] = retryEnabled.ToString(),
                [$"{KukulcanDatabaseOptions.SectionKey}:Pool:Enabled"] = poolEnabled.ToString()
            })
            .Build();
}
