using KUKULCAN.SharedKernel.Database.Tests.TestInfrastructure.internals;
using Microsoft.EntityFrameworkCore.Metadata;

namespace KUKULCAN.SharedKernel.Database.Tests.Extensions;

[TestFixture]
public sealed class ModelBuilderExtensionsTests
{
    [Test]
    public void ApplySoftDeleteFilter_WithNull_ShouldThrow()
    {
        Assert.That(
            () => ModelBuilderExtensions.ApplySoftDeleteFilter(null!),
            Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void ApplyTenantFilter_WithNullModelBuilder_ShouldThrow()
    {
        Assert.That(
            () => ModelBuilderExtensions.ApplyTenantFilter(
                null!,
                new TestTenantContext(Guid.NewGuid())),
            Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void ApplyTenantFilter_WithNullTenantContext_ShouldThrow()
    {
        var builder = new ModelBuilder();

        Assert.That(
            () => builder.ApplyTenantFilter(null!),
            Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void ApplySoftDeleteFilter_ShouldConfigureOnlySoftDeleteEntities()
    {
        var builder = new ModelBuilder();
        builder.Entity<SoftDeleteEntityForTests>();
        builder.Entity<ImmutableEntityForTests>();

        ModelBuilder returned = builder.ApplySoftDeleteFilter();

        IReadOnlyCollection<IQueryFilter> softDeleteFilters = builder.Model
            .FindEntityType(typeof(SoftDeleteEntityForTests))!
            .GetDeclaredQueryFilters();

        IReadOnlyCollection<IQueryFilter> normalFilters = builder.Model
            .FindEntityType(typeof(ImmutableEntityForTests))!
            .GetDeclaredQueryFilters();

        Assert.That(returned, Is.SameAs(builder));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(softDeleteFilters, Is.Not.Empty);
            Assert.That(normalFilters, Is.Empty);
        }
    }

    [Test]
    public void ApplyTenantFilter_ShouldConfigureGuidTenantProperty()
    {
        var tenantId = Guid.NewGuid();
        var builder = new ModelBuilder();

        builder.Entity<TenantEntityForTests>();
        builder.Entity<SoftDeleteEntityForTests>();

        ModelBuilder returned =
            builder.ApplyTenantFilter(new TestTenantContext(tenantId));

        IReadOnlyCollection<IQueryFilter> tenantFilters = builder.Model
            .FindEntityType(typeof(TenantEntityForTests))!
            .GetDeclaredQueryFilters();

        IReadOnlyCollection<IQueryFilter> softDeleteFilters = builder.Model
            .FindEntityType(typeof(SoftDeleteEntityForTests))!
            .GetDeclaredQueryFilters();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(returned, Is.SameAs(builder));
            Assert.That(tenantFilters, Is.Not.Empty);
            Assert.That(softDeleteFilters, Is.Empty);
        }
    }

    [Test]
    public void ApplyTenantFilter_ShouldIgnoreEntitiesWithoutTenantId()
    {
        var builder = new ModelBuilder();
        builder.Entity<ImmutableEntityForTests>();

        builder.ApplyTenantFilter(new TestTenantContext(Guid.NewGuid()));

        IReadOnlyCollection<IQueryFilter> filters = builder.Model
            .FindEntityType(typeof(ImmutableEntityForTests))!
            .GetDeclaredQueryFilters();

        Assert.That(filters, Is.Empty);
    }

    [Test]
    public void ApplyTenantFilter_ShouldIgnoreTenantIdWithWrongType()
    {
        var builder = new ModelBuilder();
        builder.Entity<StringTenantEntity>();

        builder.ApplyTenantFilter(new TestTenantContext(Guid.NewGuid()));

        IReadOnlyCollection<IQueryFilter> filters = builder.Model
            .FindEntityType(typeof(StringTenantEntity))!
            .GetDeclaredQueryFilters();

        Assert.That(filters, Is.Empty);
    }

    [Test]
    public void ApplyTenantFilter_ShouldIgnoreOwnedEntities()
    {
        var builder = new ModelBuilder();
        builder.Entity<OwnedTenantOwner>();
        builder.Entity<OwnedTenantOwner>().OwnsOne(x => x.Owned);

        builder.ApplyTenantFilter(new TestTenantContext(Guid.NewGuid()));

        IReadOnlyCollection<IQueryFilter> filters = builder.Model
            .FindEntityType(typeof(OwnedTenantValue))!
            .GetDeclaredQueryFilters();

        Assert.That(filters, Is.Empty);
    }

    private sealed class StringTenantEntity
    {
        public int Id { get; set; }
        public string TenantId { get; set; } = string.Empty;
    }

    private sealed class OwnedTenantOwner
    {
        public int Id { get; set; }
        public OwnedTenantValue Owned { get; set; } = new();
    }

    private sealed class OwnedTenantValue
    {
        public Guid TenantId { get; set; }
    }
}
