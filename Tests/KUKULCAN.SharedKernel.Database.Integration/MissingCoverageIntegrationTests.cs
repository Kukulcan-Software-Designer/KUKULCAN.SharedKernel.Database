using KUKULCAN.SharedKernel.Database.Extensions;
using KUKULCAN.SharedKernel.Database.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace KUKULCAN.SharedKernel.Database.Integration;

[TestFixture]
[NonParallelizable]
public sealed class MissingCoverageIntegrationTests
{
    [Test]
    public void AuditInterceptor_ShouldApplyAuditMetadataOnSynchronousSaveChanges()
    {
        Guid tenantId = Guid.NewGuid();
        using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext context =
            IntegrationTestDatabase.CreateContextAsync(tenantId).GetAwaiter().GetResult();

        var entity = new PostgreSqlDatabaseIntegrationTests.IntegrationEntity
        {
            TenantId = tenantId,
            Name = "Synchronous audit"
        };

        context.Entities.Add(entity);
        context.SaveChanges();
        entity.Name = "Synchronous audit updated";
        context.SaveChanges();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entity.CreatedOn, Is.EqualTo(PostgreSqlDatabaseIntegrationTests.FixedNow));
            Assert.That(entity.ModifiedOn, Is.EqualTo(PostgreSqlDatabaseIntegrationTests.FixedNow));
        }
    }

    [Test]
    public void SoftDeleteInterceptor_ShouldApplyLogicalDeleteOnSynchronousSaveChanges()
    {
        Guid tenantId = Guid.NewGuid();
        using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext context =
            IntegrationTestDatabase.CreateContextAsync(tenantId).GetAwaiter().GetResult();

        var entity = new PostgreSqlDatabaseIntegrationTests.IntegrationEntity
        {
            TenantId = tenantId,
            Name = "Synchronous soft delete"
        };

        context.Entities.Add(entity);
        context.SaveChanges();
        context.Entities.Remove(entity);
        int affected = context.SaveChanges();

        PostgreSqlDatabaseIntegrationTests.IntegrationEntity persisted = context.Entities
            .IgnoreQueryFilters()
            .Single(x => x.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(affected, Is.EqualTo(1));
            Assert.That(persisted.IsDeleted, Is.True);
            Assert.That(persisted.DeletedOn, Is.EqualTo(PostgreSqlDatabaseIntegrationTests.FixedNow));
            Assert.That(context.Entities.Any(x => x.Id == entity.Id), Is.False);
        }
    }

    [Test]
    public void DomainEventDispatchInterceptor_ShouldDispatchEventsThroughSynchronousSaveChanges()
    {
        Guid tenantId = Guid.NewGuid();
        var dispatcher = new Mock<IDomainEventDispatcher>();
        using var context = new PostgreSqlDatabaseIntegrationTests.IntegrationDbContext(
            Options.Create(new KukulcanDatabaseOptions
            {
                Provider = DatabaseProvider.PostgresSql,
                ConnectionString = IntegrationTestDatabase.ConnectionString,
                Retry = new KukulcanDatabaseOptions.RetryOptions { Enabled = false },
                Pool = new KukulcanDatabaseOptions.PoolOptions { Enabled = false }
            }),
            new PostgreSqlDatabaseIntegrationTests.IntegrationTenantContext(tenantId),
            new PostgreSqlDatabaseIntegrationTests.FixedClock(PostgreSqlDatabaseIntegrationTests.FixedNow),
            dispatcher.Object);

        context.Database.EnsureCreated();

        var entity = new PostgreSqlDatabaseIntegrationTests.DomainEventEntity
        {
            TenantId = tenantId,
            Name = "Synchronous event"
        };
        var domainEvent = new PostgreSqlDatabaseIntegrationTests.TestDomainEvent(
            PostgreSqlDatabaseIntegrationTests.FixedNow);
        entity.AddDomainEventForTest(domainEvent);
        context.DomainEventEntities.Add(entity);

        int affected = context.SaveChanges();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(affected, Is.EqualTo(1));
            Assert.That(entity.DomainEvents, Is.Empty);
            dispatcher.Verify(x => x.DispatchAsync(domainEvent, It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Test]
    public void SlowQueryInterceptor_ShouldLogSlowNonQueryCommand()
    {
        int previousThreshold = SlowQueryInterceptor.SlowQueryThresholdMs;
        SlowQueryInterceptor.SlowQueryThresholdMs = 0;

        try
        {
            var logger = new PostgreSqlDatabaseIntegrationTests.CapturingLogger<SlowQueryInterceptor>();
            using ServiceProvider provider = BuildServiceProvider(30, false, logger);
            using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext context =
                provider.GetRequiredService<PostgreSqlDatabaseIntegrationTests.IntegrationDbContext>();

            context.Database.ExecuteSqlRaw("SELECT pg_sleep(0.1);");

            Assert.That(logger.WarningMessages, Has.Some.Contains("[SlowQuery]"));
        }
        finally
        {
            SlowQueryInterceptor.SlowQueryThresholdMs = previousThreshold;
        }
    }

    [Test]
    public void SlowQueryInterceptor_ShouldNotLogNonQueryCommandAtOrBelowThreshold()
    {
        int previousThreshold = SlowQueryInterceptor.SlowQueryThresholdMs;
        SlowQueryInterceptor.SlowQueryThresholdMs = int.MaxValue;

        try
        {
            var logger = new PostgreSqlDatabaseIntegrationTests.CapturingLogger<SlowQueryInterceptor>();
            using ServiceProvider provider = BuildServiceProvider(30, false, logger);
            using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext context =
                provider.GetRequiredService<PostgreSqlDatabaseIntegrationTests.IntegrationDbContext>();

            context.Database.ExecuteSqlRaw("SELECT 1;");

            Assert.That(logger.WarningMessages, Has.None.Contains("[SlowQuery]"));
        }
        finally
        {
            SlowQueryInterceptor.SlowQueryThresholdMs = previousThreshold;
        }
    }

    [Test]
    public async Task KukulcanDbContextBase_ShouldEnableSensitiveDataLoggingWhenConfigured()
    {
        await using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext context =
            CreateContextWithOptions(enableSensitiveDataLogging: true, enableDetailedErrors: false);

        await context.Database.EnsureCreatedAsync();
        CoreOptionsExtension extension = context.GetService<IDbContextOptions>()
            .FindExtension<CoreOptionsExtension>()!;

        Assert.That(extension.IsSensitiveDataLoggingEnabled, Is.True);
    }

    [Test]
    public async Task KukulcanDbContextBase_ShouldEnableDetailedErrorsWhenConfigured()
    {
        await using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext context =
            CreateContextWithOptions(enableSensitiveDataLogging: false, enableDetailedErrors: true);

        await context.Database.EnsureCreatedAsync();
        CoreOptionsExtension extension = context.GetService<IDbContextOptions>()
            .FindExtension<CoreOptionsExtension>()!;

        Assert.That(extension.DetailedErrorsEnabled, Is.True);
    }

    [Test]
    public async Task KukulcanDbContextBase_ShouldApplyEntityConfigurationsFromDerivedContextAssembly()
    {
        Guid tenantId = Guid.NewGuid();
        await using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext context =
            await IntegrationTestDatabase.CreateContextAsync(tenantId);

        await context.Database.EnsureCreatedAsync();
        IEntityType? entityType = context.Model.FindEntityType(typeof(ConfiguredIntegrationEntity));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entityType, Is.Not.Null);
            Assert.That(entityType!.GetTableName(), Is.EqualTo("integration_configured_entities"));
            Assert.That(entityType.FindProperty(nameof(ConfiguredIntegrationEntity.Name))!.GetMaxLength(), Is.EqualTo(64));
        }
    }

    [Test]
    public void ApplySoftDeleteFilter_ShouldRejectNullModelBuilder()
    {
        Assert.That(
            () => ModelBuilderExtensions.ApplySoftDeleteFilter(null!),
            Throws.ArgumentNullException);
    }

    [Test]
    public void ApplyTenantFilter_ShouldRejectNullModelBuilder()
    {
        var tenantContext = new PostgreSqlDatabaseIntegrationTests.IntegrationTenantContext(Guid.NewGuid());
        Assert.That(
            () => ModelBuilderExtensions.ApplyTenantFilter(null!, tenantContext),
            Throws.ArgumentNullException);
    }

    [Test]
    public void ApplyTenantFilter_ShouldRejectNullTenantContext()
    {
        var modelBuilder = new ModelBuilder(new Microsoft.EntityFrameworkCore.Metadata.Conventions.ConventionSet());
        Assert.That(
            () => modelBuilder.ApplyTenantFilter(null!),
            Throws.ArgumentNullException);
    }

    [Test]
    public async Task ApplyTenantFilter_ShouldIgnoreEntitiesWithoutGuidTenantId()
    {
        Guid tenantId = Guid.NewGuid();
        await using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext context =
            await IntegrationTestDatabase.CreateContextAsync(tenantId);

        context.StringTenantEntities.AddRange(
            new StringTenantIntegrationEntity { TenantId = "tenant-a", Name = "A" },
            new StringTenantIntegrationEntity { TenantId = "tenant-b", Name = "B" });
        await context.SaveChangesAsync();

        List<string> visible = await context.StringTenantEntities
            .OrderBy(x => x.Name)
            .Select(x => x.Name)
            .ToListAsync();

        Assert.That(visible, Is.EqualTo(new[] { "A", "B" }));
    }

    [Test]
    public async Task ApplyTenantFilter_ShouldIgnoreOwnedEntities()
    {
        Guid tenantId = Guid.NewGuid();
        await using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext context =
            await IntegrationTestDatabase.CreateContextAsync(tenantId);

        var owner = new OwnedTenantIntegrationEntity
        {
            TenantId = tenantId,
            Name = "Owner",
            Address = new OwnedTenantAddress { TenantId = Guid.NewGuid(), Value = "Visible owned address" }
        };
        context.OwnedTenantEntities.Add(owner);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        OwnedTenantIntegrationEntity? loaded = await context.OwnedTenantEntities
            .Include(x => x.Address)
            .SingleAsync(x => x.Id == owner.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(loaded.Address, Is.Not.Null);
            Assert.That(loaded.Address!.Value, Is.EqualTo("Visible owned address"));
        }
    }

    [Test]
    public void AddKukulcanDbContext_ShouldReturnSameServiceCollection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{KukulcanDatabaseOptions.SectionKey}:Provider"] = nameof(DatabaseProvider.PostgresSql),
                [$"{KukulcanDatabaseOptions.SectionKey}:ConnectionString"] = "Host=localhost;Database=test;Username=test;Password=test"
            })
            .Build();
        var services = new ServiceCollection();

        IServiceCollection result = services.AddKukulcanDbContext<PostgreSqlDatabaseIntegrationTests.IntegrationDbContext>(configuration);

        Assert.That(result, Is.SameAs(services));
    }

    private static PostgreSqlDatabaseIntegrationTests.IntegrationDbContext CreateContextWithOptions(
        bool enableSensitiveDataLogging,
        bool enableDetailedErrors)
        => new(
            Options.Create(new KukulcanDatabaseOptions
            {
                Provider = DatabaseProvider.PostgresSql,
                ConnectionString = IntegrationTestDatabase.ConnectionString,
                EnableSensitiveDataLogging = enableSensitiveDataLogging,
                EnableDetailedErrors = enableDetailedErrors,
                Retry = new KukulcanDatabaseOptions.RetryOptions { Enabled = false },
                Pool = new KukulcanDatabaseOptions.PoolOptions { Enabled = false }
            }),
            new PostgreSqlDatabaseIntegrationTests.IntegrationTenantContext(Guid.NewGuid()),
            new PostgreSqlDatabaseIntegrationTests.FixedClock(PostgreSqlDatabaseIntegrationTests.FixedNow),
            Mock.Of<IDomainEventDispatcher>());

    private static ServiceProvider BuildServiceProvider(
        int commandTimeoutSeconds,
        bool retryEnabled,
        ILogger<SlowQueryInterceptor> logger)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{KukulcanDatabaseOptions.SectionKey}:Provider"] = nameof(DatabaseProvider.PostgresSql),
                [$"{KukulcanDatabaseOptions.SectionKey}:ConnectionString"] = IntegrationTestDatabase.ConnectionString,
                [$"{KukulcanDatabaseOptions.SectionKey}:CommandTimeoutSeconds"] = commandTimeoutSeconds.ToString(),
                [$"{KukulcanDatabaseOptions.SectionKey}:Retry:Enabled"] = retryEnabled.ToString(),
                [$"{KukulcanDatabaseOptions.SectionKey}:Retry:MaxRetryCount"] = "3",
                [$"{KukulcanDatabaseOptions.SectionKey}:Retry:MaxRetryDelaySeconds"] = "5",
                [$"{KukulcanDatabaseOptions.SectionKey}:Pool:Enabled"] = "false"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<ITenantContext>(new PostgreSqlDatabaseIntegrationTests.IntegrationTenantContext(Guid.NewGuid()));
        services.AddSingleton<IClock>(new PostgreSqlDatabaseIntegrationTests.FixedClock(PostgreSqlDatabaseIntegrationTests.FixedNow));
        services.AddSingleton<IDomainEventDispatcher>(Mock.Of<IDomainEventDispatcher>());
        services.AddSingleton(logger);
        services.AddKukulcanDbContext<PostgreSqlDatabaseIntegrationTests.IntegrationDbContext>(configuration);
        return services.BuildServiceProvider();
    }

    public sealed class ConfiguredIntegrationEntity
    {
        public int Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public sealed class ConfiguredIntegrationEntityConfiguration : IEntityTypeConfiguration<ConfiguredIntegrationEntity>
    {
        public void Configure(EntityTypeBuilder<ConfiguredIntegrationEntity> builder)
        {
            builder.ToTable("integration_configured_entities");
            builder.Property(x => x.Name).HasMaxLength(64);
        }
    }

    public sealed class StringTenantIntegrationEntity
    {
        public int Id { get; set; }
        public string TenantId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    public sealed class OwnedTenantIntegrationEntity
    {
        public int Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = string.Empty;
        public OwnedTenantAddress? Address { get; set; }
    }

    public sealed class OwnedTenantIntegrationEntityConfiguration : IEntityTypeConfiguration<OwnedTenantIntegrationEntity>
    {
        public void Configure(EntityTypeBuilder<OwnedTenantIntegrationEntity> builder)
        {
            builder.OwnsOne(x => x.Address);
        }
    }

    public sealed class OwnedTenantAddress
    {
        public Guid TenantId { get; set; }
        public string Value { get; set; } = string.Empty;
    }
}
