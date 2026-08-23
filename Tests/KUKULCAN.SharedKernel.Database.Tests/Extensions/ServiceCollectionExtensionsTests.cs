using KUKULCAN.SharedKernel.Database.Configuration;
using KUKULCAN.SharedKernel.Database.Extensions;
using KUKULCAN.SharedKernel.Database.Interceptors;
using KUKULCAN.SharedKernel.Database.UnitOfWork;
using KUKULCAN.SharedKernel.Database.Tests.Fixtures;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace KUKULCAN.SharedKernel.Database.Tests.Extensions;

/// <summary>
/// Tests for <see cref="ServiceCollectionExtensions"/>.
/// </summary>
[TestFixture]
public sealed class ServiceCollectionExtensionsTests
{
    [Test]
    public void AddKukulcanDbContext_ShouldBindConfigurationOptions()
    {
        var services = new ServiceCollection();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{KukulcanDatabaseOptions.SectionKey}:ConnectionString"] = "DataSource=test",
                [$"{KukulcanDatabaseOptions.SectionKey}:Provider"] = "SqlServer",
                [$"{KukulcanDatabaseOptions.SectionKey}:CommandTimeoutSeconds"] = "45",
                [$"{KukulcanDatabaseOptions.SectionKey}:EnableSensitiveDataLogging"] = "true",
                [$"{KukulcanDatabaseOptions.SectionKey}:Retry:Enabled"] = "false",
                [$"{KukulcanDatabaseOptions.SectionKey}:Retry:MaxRetryCount"] = "9"
            })
            .Build();

        services.AddKukulcanDbContext<TestDbContext>(configuration);

        using ServiceProvider provider = services.BuildServiceProvider();
        KukulcanDatabaseOptions options = provider
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<KukulcanDatabaseOptions>>()
            .Value;

        Assert.That(options.ConnectionString, Is.EqualTo("DataSource=test"));
        Assert.That(options.Provider, Is.EqualTo("SqlServer"));
        Assert.That(options.CommandTimeoutSeconds, Is.EqualTo(45));
        Assert.That(options.EnableSensitiveDataLogging, Is.True);
        Assert.That(options.Retry.Enabled, Is.False);
        Assert.That(options.Retry.MaxRetryCount, Is.EqualTo(9));
    }

    [Test]
    public void AddKukulcanDbContext_ShouldAttachSlowQueryInterceptorToDbContext()
    {
        var services = new ServiceCollection();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{KukulcanDatabaseOptions.SectionKey}:ConnectionString"] = "DataSource=test",
                [$"{KukulcanDatabaseOptions.SectionKey}:Provider"] = "SqlServer"
            })
            .Build();

        // AddKukulcanDbContext registers SlowQueryInterceptor, whose constructor
        // depends on ILogger<SlowQueryInterceptor>. Logging is an application-level
        // dependency and must therefore be present in this integration test's DI
        // container before the DbContext is resolved.
        services.AddLogging();
        services.AddKukulcanDbContext<TestDbContext>(configuration);
        services.AddSingleton<ITenantContext>(new TestTenantContext(Guid.NewGuid()));
        services.AddSingleton<IClock>(new TestClock(DateTimeOffset.UtcNow));
        services.AddSingleton<IDomainEventDispatcher>(Mock.Of<IDomainEventDispatcher>());

        using ServiceProvider provider = services.BuildServiceProvider();
        using TestDbContext context = provider.GetRequiredService<TestDbContext>();

        CoreOptionsExtension coreOptions = context
            .GetService<IDbContextOptions>()
            .Extensions
            .OfType<CoreOptionsExtension>()
            .Single();

        Assert.That(
            coreOptions.Interceptors,
            Has.Some.TypeOf<SlowQueryInterceptor>());
    }
}
