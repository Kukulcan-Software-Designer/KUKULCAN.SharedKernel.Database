using KUKULCAN.SharedKernel.Database.Tests.TestInfrastructure;
using KUKULCAN.SharedKernel.Database.Tests.TestInfrastructure.internals;

namespace KUKULCAN.SharedKernel.Database.Tests.Interceptors;

[TestFixture]
public sealed class AuditSaveChangesInterceptorTests
{
    private static readonly DateTimeOffset _now =
        new(2031, 2, 3, 4, 5, 6, TimeSpan.Zero);

    [Test]
    public void SavingChanges_SyncPath_WhenEntityAdded_ShouldSetCreatedOn()
    {
        using var context = CreateContext();
        var entity = new AuditableEntityForTests();
        context.Add(entity);

        context.SaveChanges();

        Assert.Multiple(() =>
        {
            Assert.That(entity.CreatedOn, Is.EqualTo(_now));
            Assert.That(entity.ModifiedOn, Is.Null);
        });
    }

    [Test]
    public async Task SavingChangesAsync_WhenEntityAdded_ShouldSetCreatedOn()
    {
        await using var context = CreateContext();
        var entity = new AuditableEntityForTests();
        context.Add(entity);

        await context.SaveChangesAsync();

        Assert.That(entity.CreatedOn, Is.EqualTo(_now));
    }

    [Test]
    public async Task SavingChangesAsync_WhenEntityModified_ShouldSetModifiedOn()
    {
        await using var context = CreateContext();
        var entity = new AuditableEntityForTests();
        context.Add(entity);
        await context.SaveChangesAsync();

        context.Entry(entity).State = EntityState.Modified;
        await context.SaveChangesAsync();

        Assert.That(entity.ModifiedOn, Is.EqualTo(_now));
    }

    [Test]
    public void SavingChanges_SyncPath_WhenEntityModified_ShouldSetModifiedOn()
    {
        using var context = CreateContext();
        var entity = new AuditableEntityForTests();
        context.Add(entity);
        context.SaveChanges();

        context.Entry(entity).State = EntityState.Modified;
        context.SaveChanges();

        Assert.That(entity.ModifiedOn, Is.EqualTo(_now));
    }

    [Test]
    public async Task SavingChangesAsync_WhenEntityUnchanged_ShouldNotSetModifiedOn()
    {
        await using var context = CreateContext();
        var entity = new AuditableEntityForTests();
        context.Add(entity);
        await context.SaveChangesAsync();
        DateTimeOffset? modifiedOnBefore = entity.ModifiedOn;

        await context.SaveChangesAsync();

        Assert.That(entity.ModifiedOn, Is.EqualTo(modifiedOnBefore));
    }

    [Test]
    public void SavingChanges_SyncPath_WhenEntityDeleted_ShouldNotSetModifiedOn()
    {
        using var context = CreateContext();
        var entity = new AuditableEntityForTests();
        context.Add(entity);
        context.SaveChanges();

        context.Remove(entity);
        context.SaveChanges();

        Assert.That(entity.ModifiedOn, Is.Null);
    }

    private static TestDbContext CreateContext()
    {
        var result = DatabaseTestContextFactory.Create(now: _now);
        return result.Context;
    }
}
