using KUKULCAN.SharedKernel.Database.Tests.TestInfrastructure;
using KUKULCAN.SharedKernel.Database.Tests.TestInfrastructure.internals;

namespace KUKULCAN.SharedKernel.Database.Tests.Extensions;

[TestFixture]
public sealed class ServiceCollectionExtensionsTests
{
    [Test]
    public void AddKukulcanDbContext_WithMissingConnectionString_ShouldThrow()
    {
        var services = new ServiceCollection();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        Assert.That(
            () => services.AddKukulcanDbContext<TestDbContext>(configuration),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.Contains("ConnectionString"));
    }

    [Test]
    public void AddKukulcanDbContext_WithValidConfiguration_ShouldRegisterExpectedServices()
    {
        var services = new ServiceCollection();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{KukulcanDatabaseOptions.SectionKey}:ConnectionString"] = "DataSource=test",
                [$"{KukulcanDatabaseOptions.SectionKey}:Provider"] = "SqlServer"
            })
            .Build();

        IServiceCollection returned = services.AddKukulcanDbContext<TestDbContext>(configuration);

        Assert.That(returned, Is.SameAs(services));
        Assert.That(
            services.Any(x => x.ServiceType == typeof(IUnitOfWork) &&
                              x.ImplementationType == typeof(UnitOfWork<TestDbContext>) &&
                              x.Lifetime == ServiceLifetime.Scoped),
            Is.True);
        Assert.That(
            services.Any(x => x.ServiceType == typeof(SlowQueryInterceptor) &&
                              x.Lifetime == ServiceLifetime.Singleton),
            Is.True);
        Assert.That(
            services.Any(x => x.ServiceType == typeof(DbContextOptions<TestDbContext>)),
            Is.True);
    }

    [Test]
    public void AddKukulcanDbContext_ShouldBindConfigurationOptions()
    {
        var services = new ServiceCollection();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{KukulcanDatabaseOptions.SectionKey}:ConnectionString"] = "DataSource=test",
                [$"{KukulcanDatabaseOptions.SectionKey}:Provider"] = "PostgresSql",
                [$"{KukulcanDatabaseOptions.SectionKey}:CommandTimeoutSeconds"] = "45",
                [$"{KukulcanDatabaseOptions.SectionKey}:EnableSensitiveDataLogging"] = "true",
                [$"{KukulcanDatabaseOptions.SectionKey}:Retry:Enabled"] = "false",
                [$"{KukulcanDatabaseOptions.SectionKey}:Retry:MaxRetryCount"] = "9"
            })
            .Build();

        services.AddKukulcanDbContext<TestDbContext>(configuration);
        using ServiceProvider provider = services.BuildServiceProvider();

        KukulcanDatabaseOptions options = provider
            .GetRequiredService<IOptions<KukulcanDatabaseOptions>>()
            .Value;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(options.Provider, Is.EqualTo(DatabaseProvider.PostgresSql));
            Assert.That(options.ConnectionString, Is.EqualTo("DataSource=test"));
            Assert.That(options.CommandTimeoutSeconds, Is.EqualTo(45));
            Assert.That(options.EnableSensitiveDataLogging, Is.True);
            Assert.That(options.Retry.Enabled, Is.False);
            Assert.That(options.Retry.MaxRetryCount, Is.EqualTo(9));
        }
    }
}
