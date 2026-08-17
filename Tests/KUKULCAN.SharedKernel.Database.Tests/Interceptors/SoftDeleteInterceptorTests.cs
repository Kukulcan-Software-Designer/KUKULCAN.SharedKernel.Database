using KUKULCAN.SharedKernel.Database.Tests.TestInfrastructure;
using KUKULCAN.SharedKernel.Database.Tests.TestInfrastructure.internals;

namespace KUKULCAN.SharedKernel.Database.Tests.Interceptors;

[TestFixture]
public sealed class SoftDeleteInterceptorTests
{
    private static readonly DateTimeOffset _now =
        new(2032, 3, 4, 5, 6, 7, TimeSpan.Zero);

    [Test]
    public async Task SavingChanges_WhenSoftDeleteEntityIsDeleted_ShouldConvertToLogicalDelete()
    {
        await using var context = CreateContext();
        var entity = new SoftDeleteEntityForTests();
        context.Add(entity);
        await context.SaveChangesAsync();

        context.Remove(entity);
        await context.SaveChangesAsync();

        Assert.Multiple(() =>
        {
            Assert.That(entity.IsDeleted, Is.True);
            Assert.That(entity.DeletedOn, Is.EqualTo(_now));
            Assert.That(context.Entry(entity).State, Is.EqualTo(EntityState.Unchanged));
        });
    }

    [Test]
    public void SavingChanges_SyncPath_ShouldConvertDelete()
    {
        using var context = CreateContext();
        var entity = new SoftDeleteEntityForTests();
        context.Add(entity);
        context.SaveChanges();

        context.Remove(entity);
        context.SaveChanges();

        Assert.That(entity.IsDeleted, Is.True);
        Assert.That(entity.DeletedOn, Is.EqualTo(_now));
    }

    private static TestDbContext CreateContext()
    {
        var result = DatabaseTestContextFactory.Create(now: _now);
        return result.Context;
    }
}
