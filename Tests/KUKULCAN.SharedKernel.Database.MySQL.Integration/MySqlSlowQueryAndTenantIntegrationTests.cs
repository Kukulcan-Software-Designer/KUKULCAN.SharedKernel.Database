namespace KUKULCAN.SharedKernel.Database.MySQL.Integration;

[TestFixture]
[NonParallelizable]
public sealed class MySqlSlowQueryAndTenantIntegrationTests
{
    [Test]
    public async Task SlowQueryInterceptor_ShouldLogRealMySqlCommandAboveThreshold()
    {
        var logger = new MySqlCapturingLogger<SlowQueryInterceptor>();
        int previous = SlowQueryInterceptor.SlowQueryThresholdMs;
        SlowQueryInterceptor.SlowQueryThresholdMs = 0;
        try
        {
            var interceptor = new SlowQueryInterceptor(logger, Options.Create(new KukulcanDatabaseOptions
            {
                Provider = DatabaseProvider.MySql,
                ConnectionString = MySqlIntegrationDatabase.ConnectionString
            }));
            await using var context = await MySqlIntegrationContextFactory.CreateAsync(Guid.NewGuid(), slowQueryInterceptor: interceptor);
            await context.Database.ExecuteSqlRawAsync("SELECT 1;");
            Assert.That(logger.WarningMessages, Has.Some.Contains("[SlowQuery]"));
        }
        finally
        {
            SlowQueryInterceptor.SlowQueryThresholdMs = previous;
        }
    }

    [Test]
    public async Task SlowQueryInterceptor_ShouldIncludeSqlWhenSensitiveDataLoggingIsEnabled()
    {
        var logger = new MySqlCapturingLogger<SlowQueryInterceptor>();
        int previous = SlowQueryInterceptor.SlowQueryThresholdMs;
        SlowQueryInterceptor.SlowQueryThresholdMs = 0;
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{KukulcanDatabaseOptions.SectionKey}:Provider"] = nameof(DatabaseProvider.MySql),
                [$"{KukulcanDatabaseOptions.SectionKey}:ConnectionString"] = MySqlIntegrationDatabase.ConnectionString,
                [$"{KukulcanDatabaseOptions.SectionKey}:EnableSensitiveDataLogging"] = "true"
            }).Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddSingleton<ITenantContext>(new MySqlTenantContext(Guid.NewGuid()));
            services.AddSingleton<IClock>(new FixedClock(MySqlIntegrationConstants.FixedNow));
            services.AddSingleton<IDomainEventDispatcher>(Mock.Of<IDomainEventDispatcher>());
            services.AddSingleton<ILogger<SlowQueryInterceptor>>(logger);
            services.AddKukulcanDbContext<MySqlIntegrationDbContext>(configuration);
            using var provider = services.BuildServiceProvider();
            await using var context = provider.GetRequiredService<MySqlIntegrationDbContext>();
            await context.Database.ExecuteSqlRawAsync("SELECT 1;");
            Assert.That(logger.WarningMessages, Has.Some.Contains("SELECT 1"));
        }
        finally
        {
            SlowQueryInterceptor.SlowQueryThresholdMs = previous;
        }
    }

    [Test]
    public async Task SlowQueryInterceptor_ShouldLogRealMySqlReaderCommandAboveThreshold()
    {
        var logger = new MySqlCapturingLogger<SlowQueryInterceptor>();
        int previous = SlowQueryInterceptor.SlowQueryThresholdMs;
        SlowQueryInterceptor.SlowQueryThresholdMs = -1;
        try
        {
            var interceptor = new SlowQueryInterceptor(logger, Options.Create(new KukulcanDatabaseOptions { Provider = DatabaseProvider.MySql, ConnectionString = MySqlIntegrationDatabase.ConnectionString }));
            await using var context = await MySqlIntegrationContextFactory.CreateAsync(Guid.NewGuid(), slowQueryInterceptor: interceptor);
            _ = context.Entities.ToList();
            Assert.That(logger.WarningMessages, Has.Some.Contains("[SlowQuery]"));
        }
        finally
        {
            SlowQueryInterceptor.SlowQueryThresholdMs = previous;
        }
    }

    [Test]
    public async Task SlowQueryInterceptor_ShouldNotLogReaderCommandAtOrBelowThreshold()
    {
        var logger = new MySqlCapturingLogger<SlowQueryInterceptor>();
        int previous = SlowQueryInterceptor.SlowQueryThresholdMs;
        SlowQueryInterceptor.SlowQueryThresholdMs = int.MaxValue;
        try
        {
            var interceptor = new SlowQueryInterceptor(logger, Options.Create(new KukulcanDatabaseOptions { Provider = DatabaseProvider.MySql, ConnectionString = MySqlIntegrationDatabase.ConnectionString }));
            await using var context = await MySqlIntegrationContextFactory.CreateAsync(Guid.NewGuid(), slowQueryInterceptor: interceptor);
            _ = context.Entities.ToList();
            Assert.That(logger.WarningMessages, Is.Empty);
        }
        finally
        {
            SlowQueryInterceptor.SlowQueryThresholdMs = previous;
        }
    }

    [Test]
    public async Task SlowQueryInterceptor_ShouldLogSynchronousNonQueryCommandAgainstRealMySql()
    {
        var logger = new MySqlCapturingLogger<SlowQueryInterceptor>();
        int previous = SlowQueryInterceptor.SlowQueryThresholdMs;
        SlowQueryInterceptor.SlowQueryThresholdMs = -1;
        try
        {
            var interceptor = new SlowQueryInterceptor(logger, Options.Create(new KukulcanDatabaseOptions { Provider = DatabaseProvider.MySql, ConnectionString = MySqlIntegrationDatabase.ConnectionString }));
            await using var context = await MySqlIntegrationContextFactory.CreateAsync(Guid.NewGuid(), slowQueryInterceptor: interceptor);
            context.Database.ExecuteSqlRaw("SELECT 1;");
            Assert.That(logger.WarningMessages, Has.Some.Contains("[SlowQuery]"));
        }
        finally
        {
            SlowQueryInterceptor.SlowQueryThresholdMs = previous;
        }
    }

    [Test]
    public async Task TenantModelCacheKeyFactory_ShouldIncludeDesignTimeInCacheKey()
    {
        await using var context = await MySqlIntegrationContextFactory.CreateAsync(Guid.NewGuid());
        Assert.That(MySqlTenantModelCacheKeyHelper.Create(context, false), Is.Not.EqualTo(MySqlTenantModelCacheKeyHelper.Create(context, true)));
    }

    [Test]
    public async Task TenantModelCacheKeyFactory_ShouldProduceDifferentKeysForDifferentTenants()
    {
        await using var first = await MySqlIntegrationContextFactory.CreateAsync(Guid.NewGuid());
        await using var second = await MySqlIntegrationContextFactory.CreateAsync(Guid.NewGuid());
        Assert.That(MySqlTenantModelCacheKeyHelper.Create(first, false), Is.Not.EqualTo(MySqlTenantModelCacheKeyHelper.Create(second, false)));
    }

    [Test]
    public async Task TenantModelCacheKeyFactory_ShouldProduceSameKeyForSameTenant()
    {
        Guid tenantId = Guid.NewGuid();
        await using var first = await MySqlIntegrationContextFactory.CreateAsync(tenantId);
        await using var second = await MySqlIntegrationContextFactory.CreateAsync(tenantId);
        Assert.That(MySqlTenantModelCacheKeyHelper.Create(first, false), Is.EqualTo(MySqlTenantModelCacheKeyHelper.Create(second, false)));
    }

    [Test]
    public void TenantModelCacheKeyFactory_ShouldRejectNullContext()
        => Assert.Throws<ArgumentNullException>(() => MySqlTenantModelCacheKeyHelper.Create(null!, false));

    [Test]
    public void TenantModelCacheKeyFactory_ShouldIgnoreTenantForNonKukulcanContext()
    {
        using var context = new DbContext(new DbContextOptionsBuilder<DbContext>().Options);
        var key = ((Type, Guid?, bool))MySqlTenantModelCacheKeyHelper.Create(context, false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(key.Item1, Is.EqualTo(typeof(DbContext)));
            Assert.That(key.Item2, Is.Null);
            Assert.That(key.Item3, Is.False);
        }
    }
}
