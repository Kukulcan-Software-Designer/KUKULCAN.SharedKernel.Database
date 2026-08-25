using NUnit.Framework;

namespace KUKULCAN.SharedKernel.Database.PostgreSQL.Integration;

public sealed class TenantModelCacheKeyFactoryIntegrationTests
{
    [Test]
    public void Create_NonKukulcanDbContext_UsesNullTenantId()
    {
        using var context = new DbContext(new DbContextOptionsBuilder<DbContext>().Options);
        var factory = new TenantModelCacheKeyFactory();

        object key = factory.Create(context, designTime: false);

        Assert.That(key, Is.TypeOf<ValueTuple<Type, Guid?, bool>>());

        (Type contextType, Guid? tenantId, bool designTime) =
            ((Type, Guid?, bool))key;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(contextType, Is.EqualTo(typeof(DbContext)));
            Assert.That(tenantId, Is.Null);
            Assert.That(designTime, Is.False);
        }
    }
}
