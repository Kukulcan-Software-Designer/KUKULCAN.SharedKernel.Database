using KUKULCAN.SharedKernel.Database.Tests.TestInfrastructure.internals;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace KUKULCAN.SharedKernel.Database.Tests;

[TestFixture]
public sealed class KukulcanDbContextBaseTests
{
    [Test]
    public void Constructor_WithNullOptions_ShouldThrow()
    {
        Assert.That(
            () => new TestDbContextWithOptions(
                null!,
                new TestTenantContext(Guid.NewGuid()),
                new TestClock(DateTimeOffset.UtcNow),
                Mock.Of<IDomainEventDispatcher>()),
            Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void Constructor_WithNullTenantContext_ShouldThrow()
    {
        Assert.That(
            () => new TestDbContextWithOptions(
                Options.Create(new KukulcanDatabaseOptions()),
                null!,
                new TestClock(DateTimeOffset.UtcNow),
                Mock.Of<IDomainEventDispatcher>()),
            Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void Constructor_WithNullClock_ShouldThrow()
    {
        Assert.That(
            () => new TestDbContextWithOptions(
                Options.Create(new KukulcanDatabaseOptions()),
                new TestTenantContext(Guid.NewGuid()),
                null!,
                Mock.Of<IDomainEventDispatcher>()),
            Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void Constructor_WithNullDispatcher_ShouldThrow()
    {
        Assert.That(
            () => new TestDbContextWithOptions(
                Options.Create(new KukulcanDatabaseOptions()),
                new TestTenantContext(Guid.NewGuid()),
                new TestClock(DateTimeOffset.UtcNow),
                null!),
            Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void Context_ShouldExposeDerivedDbSets()
    {
        using var context = DatabaseTestContextFactory.Create().Context;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(context.AuditableEntities, Is.Not.Null);
            Assert.That(context.SoftDeleteEntities, Is.Not.Null);
            Assert.That(context.ImmutableEntities, Is.Not.Null);
            Assert.That(context.TenantEntities, Is.Not.Null);
        }
    }

    [Test]
    public void OnModelCreating_ShouldApplySoftDeleteAndTenantFilters()
    {
        var result = DatabaseTestContextFactory.Create();
        using TestDbContext context = result.Context;

        IReadOnlyCollection<IQueryFilter> softDeleteFilters = context.Model
            .FindEntityType(typeof(SoftDeleteEntityForTests))!
            .GetDeclaredQueryFilters();

        IReadOnlyCollection<IQueryFilter> tenantFilters = context.Model
            .FindEntityType(typeof(TenantEntityForTests))!
            .GetDeclaredQueryFilters();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(softDeleteFilters, Is.Not.Empty);
            Assert.That(tenantFilters, Is.Not.Empty);
        }
    }

    [Test]
    public async Task TenantFilter_ShouldReturnOnlyCurrentTenant()
    {
        (TestDbContext Context, TestClock Clock, TestTenantContext Tenant, Mock<IDomainEventDispatcher> Dispatcher) result = DatabaseTestContextFactory.Create();

        await using TestDbContext context = result.Context;
        context.TenantEntities.AddRange(
            new TenantEntityForTests { TenantId = result.Tenant.TenantId },
            new TenantEntityForTests { TenantId = Guid.NewGuid() });

        await context.SaveChangesAsync();

        var visible = await context.TenantEntities.ToListAsync();

        Assert.That(visible, Has.Count.EqualTo(1));
        Assert.That(visible[0].TenantId, Is.EqualTo(result.Tenant.TenantId));
    }

    [Test]
    public async Task TenantAndSoftDeleteFilters_ShouldApplyTogether()
    {
        Guid currentTenant = Guid.NewGuid();
        Guid otherTenant = Guid.NewGuid();
        await using var context = new CombinedFilterTestDbContext(
            Options.Create(new KukulcanDatabaseOptions()),
            new TestTenantContext(currentTenant),
            new TestClock(DateTimeOffset.UtcNow),
            Mock.Of<IDomainEventDispatcher>());

        context.Entities.AddRange(
            new CombinedFilterEntity { TenantId = currentTenant, IsDeleted = false },
            new CombinedFilterEntity { TenantId = currentTenant, IsDeleted = true },
            new CombinedFilterEntity { TenantId = otherTenant, IsDeleted = false },
            new CombinedFilterEntity { TenantId = otherTenant, IsDeleted = true });

        await context.SaveChangesAsync();

        List<CombinedFilterEntity> visible = await context.Entities.ToListAsync();

        Assert.That(visible, Has.Count.EqualTo(1));
        Assert.That(visible[0].TenantId, Is.EqualTo(currentTenant));
        Assert.That(visible[0].IsDeleted, Is.False);
    }

    [Test]
    public async Task SoftDeleteFilter_ShouldHideDeletedEntities()
    {
        await using TestDbContext context = DatabaseTestContextFactory.Create().Context;
        var visible = new SoftDeleteEntityForTests { IsDeleted = false };
        var deleted = new SoftDeleteEntityForTests { IsDeleted = true };

        context.SoftDeleteEntities.AddRange(visible, deleted);
        await context.SaveChangesAsync();

        List<SoftDeleteEntityForTests> result = await context.SoftDeleteEntities.ToListAsync();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0], Is.SameAs(visible));
    }

    [Test]
    public async Task OnConfiguring_WhenOptionsAreAlreadyConfigured_ShouldNotReconfigureProvider()
    {
        await using var context = new PreconfiguredDbContext(
            Options.Create(new KukulcanDatabaseOptions()),
            new TestTenantContext(Guid.NewGuid()),
            new TestClock(DateTimeOffset.UtcNow),
            Mock.Of<IDomainEventDispatcher>());

        await context.Database.EnsureCreatedAsync();

        Assert.That(context.ConfigureProviderCalled, Is.False);
    }

    [Test]
    public void OnConfiguring_ShouldEnableSensitiveLoggingAndDetailedErrors()
    {
        var options = Options.Create(new KukulcanDatabaseOptions
        {
            EnableSensitiveDataLogging = true,
            EnableDetailedErrors = true
        });

        using var context = new ConfigurableDbContext(
            options,
            new TestTenantContext(Guid.NewGuid()),
            new TestClock(DateTimeOffset.UtcNow),
            Mock.Of<IDomainEventDispatcher>());

        CoreOptionsExtension coreOptions = context
            .GetService<IDbContextOptions>()
            .Extensions
            .OfType<CoreOptionsExtension>()
            .Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(coreOptions.IsSensitiveDataLoggingEnabled, Is.True);
            Assert.That(coreOptions.DetailedErrorsEnabled, Is.True);
        }
    }

    [Test]
    public void OnConfiguring_WithUnsupportedProvider_ShouldThrow()
    {
        var options = Options.Create(new KukulcanDatabaseOptions
        {
            Provider = (DatabaseProvider)999,
            ConnectionString = "ignored"
        });

        using var context = new UnsupportedProviderDbContext(
            options,
            new TestTenantContext(Guid.NewGuid()),
            new TestClock(DateTimeOffset.UtcNow),
            Mock.Of<IDomainEventDispatcher>());

        Assert.That(
            () => context.Database.EnsureCreated(),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.Contains("not supported"));
    }

    private sealed class ConfigurableDbContext(
        IOptions<KukulcanDatabaseOptions> options,
        ITenantContext tenantContext,
        IClock clock,
        IDomainEventDispatcher dispatcher)
        : KukulcanDbContextBase(options, tenantContext, clock, dispatcher)
    {
        protected override void ConfigureProvider(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseInMemoryDatabase(Guid.NewGuid().ToString());
    }

    private sealed class PreconfiguredDbContext(
        IOptions<KukulcanDatabaseOptions> options,
        ITenantContext tenantContext,
        IClock clock,
        IDomainEventDispatcher dispatcher)
        : KukulcanDbContextBase(options, tenantContext, clock, dispatcher)
    {
        public bool ConfigureProviderCalled { get; private set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseInMemoryDatabase(Guid.NewGuid().ToString());
            base.OnConfiguring(optionsBuilder);
        }

        protected override void ConfigureProvider(DbContextOptionsBuilder optionsBuilder)
        {
            ConfigureProviderCalled = true;
            throw new AssertionException("ConfigureProvider must not be called for preconfigured options.");
        }
    }

    private sealed class UnsupportedProviderDbContext(
        IOptions<KukulcanDatabaseOptions> options,
        ITenantContext tenantContext,
        IClock clock,
        IDomainEventDispatcher dispatcher)
        : KukulcanDbContextBase(options, tenantContext, clock, dispatcher)
    {
    }

    private sealed class CombinedFilterTestDbContext(
        IOptions<KukulcanDatabaseOptions> options,
        ITenantContext tenantContext,
        IClock clock,
        IDomainEventDispatcher dispatcher)
        : KukulcanDbContextBase(options, tenantContext, clock, dispatcher)
    {
        public DbSet<CombinedFilterEntity> Entities => Set<CombinedFilterEntity>();

        protected override void ConfigureProvider(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseInMemoryDatabase(Guid.NewGuid().ToString());
    }

    private sealed class CombinedFilterEntity : ISoftDelete
    {
        public int Id { get; set; }
        public Guid TenantId { get; set; }
        public bool IsDeleted { get; set; }
        public DateTimeOffset? DeletedOn { get; set; }
    }
}
