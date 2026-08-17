using System.Linq.Expressions;
using KUKULCAN.SharedKernel.Database.Tests.TestInfrastructure;
using KUKULCAN.SharedKernel.Database.Tests.TestInfrastructure.internals;

namespace KUKULCAN.SharedKernel.Database.Tests.Extensions;

[TestFixture]
public sealed class ModelBuilderExtensionsTests
{
    [Test]
    public void ApplySoftDeleteFilter_WithNull_ShouldThrow()
    {
        Assert.That(
            () => ModelBuilderExtensions.ApplySoftDeleteFilter(null!),
            Throws.ArgumentNullException);
    }

    [Test]
    public void ApplyTenantFilter_WithNullModelBuilder_ShouldThrow()
    {
        Assert.That(
            () => ModelBuilderExtensions.ApplyTenantFilter(null!, new TestTenantContext(Guid.NewGuid())),
            Throws.ArgumentNullException);
    }

    [Test]
    public void ApplyTenantFilter_WithNullTenantContext_ShouldThrow()
    {
        var builder = new ModelBuilder();

        Assert.That(
            () => builder.ApplyTenantFilter(null!),
            Throws.ArgumentNullException);
    }

    [Test]
    public void ApplySoftDeleteFilter_ShouldConfigureOnlySoftDeleteEntities()
    {
        var builder = new ModelBuilder();
        builder.Entity<SoftDeleteEntityForTests>();
        builder.Entity<ImmutableEntityForTests>();

        ModelBuilder returned = builder.ApplySoftDeleteFilter();

        Assert.That(returned, Is.SameAs(builder));
        LambdaExpression? softFilter = builder.Model.FindEntityType(typeof(SoftDeleteEntityForTests))!.GetQueryFilter();
        LambdaExpression? normalFilter = builder.Model.FindEntityType(typeof(ImmutableEntityForTests))!.GetQueryFilter();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(softFilter, Is.Not.Null);
            Assert.That(normalFilter, Is.Null);
        }
    }

    [Test]
    public void ApplyTenantFilter_ShouldConfigureGuidTenantProperty()
    {
        var tenantId = Guid.NewGuid();
        var builder = new ModelBuilder();
        builder.Entity<TenantEntityForTests>();
        builder.Entity<SoftDeleteEntityForTests>();

        ModelBuilder returned = builder.ApplyTenantFilter(new TestTenantContext(tenantId));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(returned, Is.SameAs(builder));
            Assert.That(
                builder.Model.FindEntityType(typeof(TenantEntityForTests))!.GetQueryFilter(),
                Is.Not.Null);
            Assert.That(
                builder.Model.FindEntityType(typeof(SoftDeleteEntityForTests))!.GetQueryFilter(),
                Is.Null);
        }
    }
}
