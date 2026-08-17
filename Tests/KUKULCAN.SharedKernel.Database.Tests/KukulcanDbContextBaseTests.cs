using KUKULCAN.SharedKernel.Database.Tests.TestInfrastructure.internals;
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
}
