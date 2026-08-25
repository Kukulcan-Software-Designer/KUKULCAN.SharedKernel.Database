namespace KUKULCAN.SharedKernel.Database.MySQL.Integration;

[TestFixture]
[NonParallelizable]
public sealed class MySqlProviderBehaviorParityIntegrationTests
{
    [Test]
    public async Task CommandTimeout_ShouldBeAppliedToRealMySqlContext()
    {
        using ServiceProvider provider = BuildProvider(retryEnabled: false, timeoutSeconds: 47);
        using IServiceScope scope = provider.CreateScope();
        await using MySqlIntegrationDbContext context = scope.ServiceProvider.GetRequiredService<MySqlIntegrationDbContext>();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(context.Database.ProviderName, Is.EqualTo("MySql.EntityFrameworkCore"));
            Assert.That(context.Database.GetCommandTimeout(), Is.EqualTo(47));
        }
    }

    [Test]
    public async Task RetryExecutionStrategy_ShouldRetryAfterTransientTimeout()
    {
        using ServiceProvider provider = BuildProvider(retryEnabled: true, timeoutSeconds: 30);
        using IServiceScope scope = provider.CreateScope();
        await using MySqlIntegrationDbContext context = scope.ServiceProvider.GetRequiredService<MySqlIntegrationDbContext>();

        Microsoft.EntityFrameworkCore.Storage.IExecutionStrategy strategy = context.Database.CreateExecutionStrategy();
        int attempts = 0;

        int result = await strategy.ExecuteAsync(async () =>
        {
            attempts++;

            if (attempts == 1)
                throw new TimeoutException("Synthetic transient failure for retry contract coverage.");

            await context.Database.ExecuteSqlRawAsync("SELECT 1;");
            return 42;
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(strategy.RetriesOnFailure, Is.True);
            Assert.That(attempts, Is.EqualTo(2));
            Assert.That(result, Is.EqualTo(42));
        }
    }

    private static ServiceProvider BuildProvider(bool retryEnabled, int timeoutSeconds)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{KukulcanDatabaseOptions.SectionKey}:Provider"] = nameof(DatabaseProvider.MySql),
                [$"{KukulcanDatabaseOptions.SectionKey}:ConnectionString"] = MySqlIntegrationDatabase.ConnectionString,
                [$"{KukulcanDatabaseOptions.SectionKey}:CommandTimeoutSeconds"] = timeoutSeconds.ToString(),
                [$"{KukulcanDatabaseOptions.SectionKey}:Retry:Enabled"] = retryEnabled.ToString(),
                [$"{KukulcanDatabaseOptions.SectionKey}:Retry:MaxRetryCount"] = "2",
                [$"{KukulcanDatabaseOptions.SectionKey}:Retry:MaxRetryDelaySeconds"] = "1",
                [$"{KukulcanDatabaseOptions.SectionKey}:Pool:Enabled"] = "false"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(new MySqlTenantContext(Guid.NewGuid()));
        services.AddSingleton<IClock>(new FixedClock(MySqlIntegrationConstants.FixedNow));
        services.AddSingleton<IDomainEventDispatcher>(Mock.Of<IDomainEventDispatcher>());
        services.AddKukulcanDbContext<MySqlIntegrationDbContext>(configuration);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
