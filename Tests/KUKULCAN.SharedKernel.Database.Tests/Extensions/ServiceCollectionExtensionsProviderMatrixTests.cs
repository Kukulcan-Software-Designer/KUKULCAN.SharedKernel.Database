using KUKULCAN.SharedKernel.Database.Tests.TestInfrastructure.internals;
using Microsoft.Extensions.Hosting;

namespace KUKULCAN.SharedKernel.Database.Tests.Extensions;

[TestFixture]
public sealed class ServiceCollectionExtensionsProviderMatrixTests
{
    private static readonly object[][] ProviderCases =
    [
        [DatabaseProvider.SqlServer, "Server=localhost;Database=KukulcanTests;Integrated Security=True;", "Microsoft.EntityFrameworkCore.SqlServer"],
        [DatabaseProvider.PostgresSql, "Host=localhost;Database=KukulcanTests;Username=test;Password=test;", "Npgsql.EntityFrameworkCore.PostgreSQL"],
        [DatabaseProvider.MySql, "Server=localhost;Database=KukulcanTests;User Id=test;Password=test;", "MySql.EntityFrameworkCore"]
    ];

    [TestCaseSource(nameof(ProviderCases))]
    public void AddKukulcanDbContext_ShouldResolveConfiguredProviderForEverySupportedDatabase(
        DatabaseProvider provider,
        string connectionString,
        string expectedProviderName)
    {
        var services = new ServiceCollection();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{KukulcanDatabaseOptions.SectionKey}:Provider"] = provider.ToString(),
                [$"{KukulcanDatabaseOptions.SectionKey}:ConnectionString"] = connectionString,
                [$"{KukulcanDatabaseOptions.SectionKey}:Retry:Enabled"] = "false"
            })
            .Build();

        services.AddLogging();
        services.AddKukulcanDbContext<TestDbContext>(configuration);
        services.AddScoped<ITenantContext>(_ => new TestTenantContext(Guid.NewGuid()));
        services.AddScoped<IClock>(_ => new TestClock(DateTimeOffset.UtcNow));
        services.AddScoped<IDomainEventDispatcher>(_ => Mock.Of<IDomainEventDispatcher>());

        using ServiceProvider serviceProvider = services.BuildServiceProvider();
        using TestDbContext context = serviceProvider.GetRequiredService<TestDbContext>();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(context.Database.ProviderName, Is.EqualTo(expectedProviderName));
            Assert.That(serviceProvider.GetRequiredService<IUnitOfWork>(), Is.TypeOf<UnitOfWork<TestDbContext>>());
            Assert.That(serviceProvider.GetServices<IHostedService>(), Has.Some.TypeOf<KukulcanDatabaseStartupHostedService<TestDbContext>>());
            Assert.That(serviceProvider.GetRequiredService<KukulcanDatabaseStartupInitializer<TestDbContext>>(), Is.Not.Null);
        }
    }
}
