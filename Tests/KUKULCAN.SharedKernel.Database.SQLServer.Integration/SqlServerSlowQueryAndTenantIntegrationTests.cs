namespace KUKULCAN.SharedKernel.Database.Integration.SQLServer;

[TestFixture]
[NonParallelizable]
public sealed class SqlServerSlowQueryAndTenantIntegrationTests
{
    [Test]
    public async Task SlowQueryInterceptor_ShouldLogRealSqlServerCommandAboveThreshold()
    {
        var logger = new SqlServerCapturingLogger<SlowQueryInterceptor>();
        int previous = SlowQueryInterceptor.SlowQueryThresholdMs;
        SlowQueryInterceptor.SlowQueryThresholdMs = 0;
        try
        {
            var interceptor = new SlowQueryInterceptor(logger, Options.Create(new KukulcanDatabaseOptions
            {
                Provider = DatabaseProvider.SqlServer,
                ConnectionString = SqlServerIntegrationDatabase.ConnectionString
            }));
            await using var context = await SqlServerIntegrationContextFactory.CreateAsync(Guid.NewGuid(), slowQueryInterceptor: interceptor);
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
        var logger = new SqlServerCapturingLogger<SlowQueryInterceptor>();
        int previous = SlowQueryInterceptor.SlowQueryThresholdMs;
        SlowQueryInterceptor.SlowQueryThresholdMs = 0;
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{KukulcanDatabaseOptions.SectionKey}:Provider"] = nameof(DatabaseProvider.SqlServer),
                [$"{KukulcanDatabaseOptions.SectionKey}:ConnectionString"] = SqlServerIntegrationDatabase.ConnectionString,
                [$"{KukulcanDatabaseOptions.SectionKey}:EnableSensitiveDataLogging"] = "true"
            }).Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddSingleton<ITenantContext>(new SqlServerTenantContext(Guid.NewGuid()));
            services.AddSingleton<IClock>(new FixedClock(SqlServerIntegrationConstants.FixedNow));
            services.AddSingleton<IDomainEventDispatcher>(Mock.Of<IDomainEventDispatcher>());
            services.AddSingleton<ILogger<SlowQueryInterceptor>>(logger);
            services.AddKukulcanDbContext<SqlServerIntegrationDbContext>(configuration);
            using var provider = services.BuildServiceProvider();
            await using var context = provider.GetRequiredService<SqlServerIntegrationDbContext>();
            await context.Database.ExecuteSqlRawAsync("SELECT 1;");
            Assert.That(logger.WarningMessages, Has.Some.Contains("SELECT 1"));
        }
        finally
        {
            SlowQueryInterceptor.SlowQueryThresholdMs = previous;
        }
    }

    [Test]
    public async Task SlowQueryInterceptor_ShouldLogRealSqlServerReaderCommandAboveThreshold()
    {
        var logger = new SqlServerCapturingLogger<SlowQueryInterceptor>();
        int previous = SlowQueryInterceptor.SlowQueryThresholdMs;
        SlowQueryInterceptor.SlowQueryThresholdMs = -1;
        try
        {
            var interceptor = new SlowQueryInterceptor(logger, Options.Create(new KukulcanDatabaseOptions { Provider = DatabaseProvider.SqlServer, ConnectionString = SqlServerIntegrationDatabase.ConnectionString }));
            await using var context = await SqlServerIntegrationContextFactory.CreateAsync(Guid.NewGuid(), slowQueryInterceptor: interceptor);
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
        var logger = new SqlServerCapturingLogger<SlowQueryInterceptor>();
        int previous = SlowQueryInterceptor.SlowQueryThresholdMs;
        SlowQueryInterceptor.SlowQueryThresholdMs = int.MaxValue;
        try
        {
            var interceptor = new SlowQueryInterceptor(logger, Options.Create(new KukulcanDatabaseOptions { Provider = DatabaseProvider.SqlServer, ConnectionString = SqlServerIntegrationDatabase.ConnectionString }));
            await using var context = await SqlServerIntegrationContextFactory.CreateAsync(Guid.NewGuid(), slowQueryInterceptor: interceptor);
            _ = context.Entities.ToList();
            Assert.That(logger.WarningMessages, Is.Empty);
        }
        finally
        {
            SlowQueryInterceptor.SlowQueryThresholdMs = previous;
        }
    }

    [Test]
    public async Task SlowQueryInterceptor_ShouldLogSynchronousReaderCommandAgainstRealSqlServer()
    {
        var logger = new SqlServerCapturingLogger<SlowQueryInterceptor>();
        int previous = SlowQueryInterceptor.SlowQueryThresholdMs;
        SlowQueryInterceptor.SlowQueryThresholdMs = -1;
        try
        {
            var interceptor = new SlowQueryInterceptor(logger, Options.Create(new KukulcanDatabaseOptions { Provider = DatabaseProvider.SqlServer, ConnectionString = SqlServerIntegrationDatabase.ConnectionString }));
            await using var context = await SqlServerIntegrationContextFactory.CreateAsync(Guid.NewGuid(), slowQueryInterceptor: interceptor);
            _ = context.Entities.ToList();
            Assert.That(logger.WarningMessages, Has.Some.Contains("[SlowQuery]"));
        }
        finally
        {
            SlowQueryInterceptor.SlowQueryThresholdMs = previous;
        }
    }

    [Test]
    public async Task SlowQueryInterceptor_ShouldLogSynchronousNonQueryCommandAgainstRealSqlServer()
    {
        var logger = new SqlServerCapturingLogger<SlowQueryInterceptor>();
        int previous = SlowQueryInterceptor.SlowQueryThresholdMs;
        SlowQueryInterceptor.SlowQueryThresholdMs = -1;
        try
        {
            var interceptor = new SlowQueryInterceptor(logger, Options.Create(new KukulcanDatabaseOptions { Provider = DatabaseProvider.SqlServer, ConnectionString = SqlServerIntegrationDatabase.ConnectionString }));
            await using var context = await SqlServerIntegrationContextFactory.CreateAsync(Guid.NewGuid(), slowQueryInterceptor: interceptor);
            context.Database.ExecuteSqlRaw("SELECT 1;");
            Assert.That(logger.WarningMessages, Has.Some.Contains("[SlowQuery]"));
        }
        finally
        {
            SlowQueryInterceptor.SlowQueryThresholdMs = previous;
        }
    }

    [Test]
    public void TenantModelCacheKeyFactory_ShouldIgnoreTenantForNonKukulcanContext()
    {
        using var context = new DbContext(new DbContextOptionsBuilder<DbContext>().Options);
        var key = ((Type, Guid?, bool))new TenantModelCacheKeyFactory().Create(context, false);
        Assert.That(key.Item2, Is.Null);
    }

    [Test]
    public async Task TenantModelCacheKeyFactory_ShouldIncludeDesignTimeInCacheKey()
    {
        await using var context = await SqlServerIntegrationContextFactory.CreateAsync(Guid.NewGuid());
        var factory = new TenantModelCacheKeyFactory();
        Assert.That(factory.Create(context, false), Is.Not.EqualTo(factory.Create(context, true)));
    }

    [Test]
    public void TenantModelCacheKeyFactory_ShouldKeepNonKukulcanDesignTimeKeysDistinct()
    {
        using var context = new DbContext(new DbContextOptionsBuilder<DbContext>().Options);
        var factory = new TenantModelCacheKeyFactory();
        Assert.That(factory.Create(context, false), Is.Not.EqualTo(factory.Create(context, true)));
    }

    [Test]
    public async Task TenantModelCacheKeyFactory_ShouldProduceDifferentKeysForDifferentTenants()
    {
        await using var first = await SqlServerIntegrationContextFactory.CreateAsync(Guid.NewGuid());
        await using var second = await SqlServerIntegrationContextFactory.CreateAsync(Guid.NewGuid());
        var factory = new TenantModelCacheKeyFactory();
        Assert.That(factory.Create(first, false), Is.Not.EqualTo(factory.Create(second, false)));
    }

    [Test]
    public async Task TenantModelCacheKeyFactory_ShouldProduceSameKeyForSameTenantAndDesignTime()
    {
        Guid tenantId = Guid.NewGuid();
        await using var first = await SqlServerIntegrationContextFactory.CreateAsync(tenantId);
        await using var second = await SqlServerIntegrationContextFactory.CreateAsync(tenantId);
        var factory = new TenantModelCacheKeyFactory();
        Assert.That(factory.Create(first, false), Is.EqualTo(factory.Create(second, false)));
    }

    [Test]
    public void TenantModelCacheKeyFactory_ShouldRejectNullContext()
        => Assert.Throws<ArgumentNullException>(() => new TenantModelCacheKeyFactory().Create(null!, false));

    [Test]
    public void Create_NonKukulcanDbContext_UsesNullTenantId()
    {
        using var context = new DbContext(new DbContextOptionsBuilder<DbContext>().Options);
        var key = (ValueTuple<Type, Guid?, bool>)new TenantModelCacheKeyFactory().Create(context, false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(key.Item1, Is.EqualTo(typeof(DbContext)));
            Assert.That(key.Item2, Is.Null);
            Assert.That(key.Item3, Is.False);
        }
    }
}
