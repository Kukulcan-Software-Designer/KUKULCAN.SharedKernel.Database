using KUKULCAN.SharedKernel.Database.Tests.TestInfrastructure;
using KUKULCAN.SharedKernel.Database.Tests.TestInfrastructure.internals;

namespace KUKULCAN.SharedKernel.Database.Tests.Interceptors;

[TestFixture]
public sealed class ImmutableEntityInterceptorTests
{
    [Test]
    public async Task SavingChanges_WhenImmutableEntityIsAdded_ShouldSucceed()
    {
        await using var context = DatabaseTestContextFactory.Create().Context;

        context.Add(new ImmutableEntityForTests { Value = "original" });

        Assert.That(await context.SaveChangesAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task SavingChanges_WhenImmutableEntityIsModified_ShouldThrow()
    {
        await using var context = DatabaseTestContextFactory.Create().Context;
        var entity = new ImmutableEntityForTests { Value = "original" };
        context.Add(entity);
        await context.SaveChangesAsync();

        entity.Value = "changed";

        Assert.That(
            () => context.SaveChangesAsync(),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.Contains("immutable"));
    }

    [Test]
    public void SavingChanges_SyncPath_WhenImmutableEntityIsDeleted_ShouldThrow()
    {
        using var context = DatabaseTestContextFactory.Create().Context;
        var entity = new ImmutableEntityForTests { Value = "original" };
        context.Add(entity);
        context.SaveChanges();

        context.Remove(entity);

        Assert.That(
            context.SaveChanges,
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.Contains("immutable"));
    }
}
