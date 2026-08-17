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
                              x.ImplementationType == typeof(UnitOfWork<TestDbContext>)),
            Is.True);
        Assert.That(
            services.Any(x => x.ServiceType == typeof(SlowQueryInterceptor)),
            Is.True);
        Assert.That(
            services.Any(x => x.ServiceType == typeof(DbContextOptions<TestDbContext>)),
            Is.True);
    }
}
