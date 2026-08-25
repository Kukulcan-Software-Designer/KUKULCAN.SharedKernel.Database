namespace KUKULCAN.SharedKernel.Database.MySQL.Integration;

[TestFixture]
[NonParallelizable]
public sealed class MySqlProviderAndConfigurationIntegrationTests
{
    [Test]
    public void ConfigureProvider_ShouldRejectUnsupportedProvider()
    {
        var options = Options.Create(new KukulcanDatabaseOptions { Provider = (DatabaseProvider)999, ConnectionString = MySqlIntegrationDatabase.ConnectionString });
        using var context = new MySqlIntegrationDbContext(options, new MySqlTenantContext(Guid.NewGuid()), new FixedClock(MySqlIntegrationConstants.FixedNow), Mock.Of<IDomainEventDispatcher>());
        Assert.Throws<NotSupportedException>(() => _ = context.Database.ProviderName);
    }

    [Test]
    public async Task ConfigureProvider_ShouldUseMySqlWhenProviderInstalled()
    {
        await using var context = await MySqlIntegrationContextFactory.CreateAsync(Guid.NewGuid());
        Assert.That(context.Database.ProviderName, Is.EqualTo("MySql.EntityFrameworkCore"));
    }

    [Test]
    public void AddKukulcanDbContext_ShouldRejectMissingConnectionString()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{KukulcanDatabaseOptions.SectionKey}:Provider"] = nameof(DatabaseProvider.MySql)
        }).Build();
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddKukulcanDbContext<MySqlIntegrationDbContext>(configuration));
    }

    [Test]
    public void AddKukulcanDbContext_ShouldRejectWhitespaceConnectionString()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{KukulcanDatabaseOptions.SectionKey}:Provider"] = nameof(DatabaseProvider.MySql),
            [$"{KukulcanDatabaseOptions.SectionKey}:ConnectionString"] = "   "
        }).Build();
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddKukulcanDbContext<MySqlIntegrationDbContext>(configuration));
    }

    [Test]
    public void AddKukulcanDbContext_ShouldRegisterInfrastructureWithExpectedLifetimes()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        Assert.That(scope.ServiceProvider.GetRequiredService<MySqlIntegrationDbContext>(), Is.Not.Null);
        Assert.That(scope.ServiceProvider.GetRequiredService<SlowQueryInterceptor>(), Is.Not.Null);
        Assert.That(scope.ServiceProvider.GetRequiredService<IOptions<KukulcanDatabaseOptions>>(), Is.Not.Null);
    }

    [Test]
    public void AddKukulcanDbContext_ShouldRegisterUnitOfWorkAsScopedService()
    {
        using var provider = BuildProvider();
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        Assert.That(first.ServiceProvider.GetRequiredService<IUnitOfWork>(), Is.Not.SameAs(second.ServiceProvider.GetRequiredService<IUnitOfWork>()));
    }

    [Test]
    public void AddKukulcanDbContext_ShouldResolveOneContextPerScope()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        Assert.That(scope.ServiceProvider.GetRequiredService<MySqlIntegrationDbContext>(), Is.SameAs(scope.ServiceProvider.GetRequiredService<MySqlIntegrationDbContext>()));
    }

    [Test]
    public void AddKukulcanDbContext_ShouldBindAllNestedDatabaseOptions()
    {
        using var provider = BuildProvider(retryEnabled: true, timeout: 42);
        var value = provider.GetRequiredService<IOptions<KukulcanDatabaseOptions>>().Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(value.Provider, Is.EqualTo(DatabaseProvider.MySql));
            Assert.That(value.CommandTimeoutSeconds, Is.EqualTo(42));
            Assert.That(value.Retry.Enabled, Is.True);
        }
    }

    [Test]
    public void AddKukulcanDbContext_ShouldBindDatabaseOptionsFromConfiguration()
    {
        using var provider = BuildProvider(timeout: 37);
        Assert.That(provider.GetRequiredService<IOptions<KukulcanDatabaseOptions>>().Value.CommandTimeoutSeconds, Is.EqualTo(37));
    }

    [Test]
    public void AddKukulcanDbContext_ShouldPreserveDefaultNestedOptionValues()
    {
        using var provider = BuildProvider();
        Assert.That(provider.GetRequiredService<IOptions<KukulcanDatabaseOptions>>().Value.Pool.Enabled, Is.False);
    }

    private static ServiceProvider BuildProvider(bool retryEnabled = false, int timeout = 30)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{KukulcanDatabaseOptions.SectionKey}:Provider"] = nameof(DatabaseProvider.MySql),
            [$"{KukulcanDatabaseOptions.SectionKey}:ConnectionString"] = MySqlIntegrationDatabase.ConnectionString,
            [$"{KukulcanDatabaseOptions.SectionKey}:CommandTimeoutSeconds"] = timeout.ToString(),
            [$"{KukulcanDatabaseOptions.SectionKey}:Retry:Enabled"] = retryEnabled.ToString(),
            [$"{KukulcanDatabaseOptions.SectionKey}:Retry:MaxRetryCount"] = "2",
            [$"{KukulcanDatabaseOptions.SectionKey}:Retry:MaxRetryDelaySeconds"] = "5",
            [$"{KukulcanDatabaseOptions.SectionKey}:Pool:Enabled"] = "false",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<ITenantContext>(new MySqlTenantContext(Guid.NewGuid()));
        services.AddSingleton<IClock>(new FixedClock(MySqlIntegrationConstants.FixedNow));
        services.AddSingleton<IDomainEventDispatcher>(Mock.Of<IDomainEventDispatcher>());
        services.AddKukulcanDbContext<MySqlIntegrationDbContext>(configuration);
        return services.BuildServiceProvider();
    }
}
