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
        services.AddKukulcanDbContext<ProviderMatrixDbContext>(configuration);
        services.AddScoped<ITenantContext>(_ => new TestTenantContext(Guid.NewGuid()));
        services.AddScoped<IClock>(_ => new TestClock(DateTimeOffset.UtcNow));
        services.AddScoped<IDomainEventDispatcher>(_ => Mock.Of<IDomainEventDispatcher>());

        using ServiceProvider serviceProvider = services.BuildServiceProvider();
        using IServiceScope scope = serviceProvider.CreateScope();
        using ProviderMatrixDbContext context = scope.ServiceProvider.GetRequiredService<ProviderMatrixDbContext>();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(context.Database.ProviderName, Is.EqualTo(expectedProviderName));
            Assert.That(scope.ServiceProvider.GetRequiredService<IUnitOfWork>(), Is.TypeOf<UnitOfWork<ProviderMatrixDbContext>>());
        }

        Assert.That(
            serviceProvider.GetServices<IHostedService>(),
            Has.Some.TypeOf<KukulcanDatabaseStartupHostedService<ProviderMatrixDbContext>>());
    }

    private sealed class ProviderMatrixDbContext(
        IOptions<KukulcanDatabaseOptions> options,
        ITenantContext tenantContext,
        IClock clock,
        IDomainEventDispatcher dispatcher,
        SlowQueryInterceptor? slowQueryInterceptor = null)
        : KukulcanDbContextBase(options, tenantContext, clock, dispatcher, slowQueryInterceptor);
}
