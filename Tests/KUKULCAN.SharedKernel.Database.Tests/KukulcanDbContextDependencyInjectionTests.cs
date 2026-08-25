using KUKULCAN.SharedKernel.Database.Configuration;
using KUKULCAN.SharedKernel.Database.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
        services.AddSingleton<TestTenantContext>();
        services.AddSingleton<ITenantContext>(sp => sp.GetRequiredService<TestTenantContext>());
        services.AddSingleton<TestClock>();
        services.AddSingleton<IClock>(sp => sp.GetRequiredService<TestClock>());
        services.AddSingleton<IDomainEventDispatcher, TestDomainEventDispatcher>();
        services.AddKukulcanDbContext<DependencyInjectionTestDbContext>(configuration);

        using var serviceProvider = services.BuildServiceProvider();
        using var context = serviceProvider.GetRequiredService<DependencyInjectionTestDbContext>();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(context.Database.ProviderName, Is.EqualTo(expectedProvider));
            Assert.That(context.Database.GetCommandTimeout(), Is.EqualTo(37));
        }

        Assert.That(context.Database.CreateExecutionStrategy().RetriesOnFailure, Is.True);
    }

    private sealed class DependencyInjectionTestDbContext(
        IOptions<KukulcanDatabaseOptions> options,
        ITenantContext tenantContext,
        IClock clock,
        IDomainEventDispatcher dispatcher,
        SlowQueryInterceptor slowQueryInterceptor)
        : KukulcanDbContextBase(options, tenantContext, clock, dispatcher, slowQueryInterceptor);

    private sealed class TestTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId { get; } = tenantId;
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

    private sealed class TestDomainEventDispatcher : IDomainEventDispatcher
    {
        public Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
