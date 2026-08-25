using KUKULCAN.SharedKernel.Database.TestInfrastructure.internals;

namespace KUKULCAN.SharedKernel.Database.Tests;

[TestFixture]
public sealed class KukulcanDbContextDependencyInjectionTests
{
    [TestCase(DatabaseProvider.SqlServer, "Microsoft.EntityFrameworkCore.SqlServer", "Server=localhost;Database=KukulcanTests;Integrated Security=True;")]
    [TestCase(DatabaseProvider.PostgresSql, "Npgsql.EntityFrameworkCore.PostgreSQL", "Host=localhost;Database=KukulcanTests;Username=test;Password=test;")]
    [TestCase(DatabaseProvider.MySql, "MySql.EntityFrameworkCore", "Server=localhost;Database=KukulcanTests;User Id=test;Password=test;")]
    public void AddKukulcanDbContext_ShouldConfigureSelectedProviderThroughDependencyInjection(
        DatabaseProvider provider,
        string expectedProvider,
        string connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [KukulcanDatabaseOptions.SectionKey + ":Provider"] = provider.ToString(),
                [KukulcanDatabaseOptions.SectionKey + ":ConnectionString"] = connectionString,
                [KukulcanDatabaseOptions.SectionKey + ":CommandTimeoutSeconds"] = "37",
                [KukulcanDatabaseOptions.SectionKey + ":Retry:Enabled"] = "true",
                [KukulcanDatabaseOptions.SectionKey + ":Retry:MaxRetryCount"] = "5",
                [KukulcanDatabaseOptions.SectionKey + ":Retry:MaxRetryDelaySeconds"] = "7"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext>(_ => new TestTenantContext(Guid.NewGuid()));
        services.AddSingleton<IClock>(_ => new TestClock(DateTimeOffset.UtcNow));
        services.AddSingleton<IDomainEventDispatcher>(_ => Mock.Of<IDomainEventDispatcher>());
        services.AddKukulcanDbContext<DependencyInjectionTestDbContext>(configuration);

        using var serviceProvider = services.BuildServiceProvider();
        using var context = serviceProvider.GetRequiredService<DependencyInjectionTestDbContext>();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(context.Database.ProviderName, Is.EqualTo(expectedProvider));
            Assert.That(context.Database.GetCommandTimeout(), Is.EqualTo(37));
            Assert.That(context.Database.CreateExecutionStrategy().RetriesOnFailure, Is.True);
        }
    }

    private sealed class DependencyInjectionTestDbContext(
        IOptions<KukulcanDatabaseOptions> options,
        ITenantContext tenantContext,
        IClock clock,
        IDomainEventDispatcher dispatcher,
        SlowQueryInterceptor slowQueryInterceptor)
        : KukulcanDbContextBase(options, tenantContext, clock, dispatcher, slowQueryInterceptor);
}
