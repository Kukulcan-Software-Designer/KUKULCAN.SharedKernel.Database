using KUKULCAN.SharedKernel.Database.Tests.TestInfrastructure.internals;

namespace KUKULCAN.SharedKernel.Database.Tests;

[TestFixture]
public sealed class TenantModelCacheKeyFactoryTests
{
    private const string DatabaseName = "TenantModelCacheKeyFactoryTests";

    [SetUp]
    public async Task SetUp()
    {
        await using var context = CreateContext(Guid.NewGuid());
        await context.Database.EnsureDeletedAsync();
    }

    [Test]
    public async Task TenantSpecificModels_ShouldReturnOnlyRowsForCurrentTenant()
    {
        Guid tenantA = Guid.NewGuid();
        Guid tenantB = Guid.NewGuid();

        await using (TenantScopedDbContext seedContext = CreateContext(tenantA))
        {
            await seedContext.Database.EnsureCreatedAsync();
            seedContext.TenantEntities.AddRange(
                new TenantEntityForTests { TenantId = tenantA },
                new TenantEntityForTests { TenantId = tenantB });
            await seedContext.SaveChangesAsync();
        }

        await using (TenantScopedDbContext contextA = CreateContext(tenantA))
        await using (TenantScopedDbContext contextB = CreateContext(tenantB))
        {
            List<TenantEntityForTests> visibleA = await contextA.TenantEntities.ToListAsync();
            List<TenantEntityForTests> visibleB = await contextB.TenantEntities.ToListAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(visibleA, Has.Count.EqualTo(1));
                Assert.That(visibleA[0].TenantId, Is.EqualTo(tenantA));
                Assert.That(visibleB, Has.Count.EqualTo(1));
                Assert.That(visibleB[0].TenantId, Is.EqualTo(tenantB));
            }
        }
    }

    [Test]
    public void DifferentTenants_ShouldProduceIndependentModels()
    {
        Guid tenantA = Guid.NewGuid();
        Guid tenantB = Guid.NewGuid();

        using TenantScopedDbContext contextA = CreateContext(tenantA);
        using TenantScopedDbContext contextB = CreateContext(tenantB);

        Assert.That(contextA.Model, Is.Not.SameAs(contextB.Model));
    }

    private static TenantScopedDbContext CreateContext(Guid tenantId)
        => new(
            Options.Create(new KukulcanDatabaseOptions
            {
                Provider = DatabaseProvider.SqlServer,
                ConnectionString = "ignored"
            }),
            new TestTenantContext(tenantId),
            new TestClock(DateTimeOffset.UtcNow),
            Mock.Of<IDomainEventDispatcher>());

    private sealed class TenantScopedDbContext(
        IOptions<KukulcanDatabaseOptions> options,
        ITenantContext tenantContext,
        IClock clock,
        IDomainEventDispatcher dispatcher)
        : KukulcanDbContextBase(options, tenantContext, clock, dispatcher)
    {
        public DbSet<TenantEntityForTests> TenantEntities => Set<TenantEntityForTests>();

        protected override void ConfigureProvider(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseInMemoryDatabase(DatabaseName);
    }
}
