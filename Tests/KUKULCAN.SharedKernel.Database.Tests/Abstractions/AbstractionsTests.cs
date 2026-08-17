using KUKULCAN.SharedKernel.Database.Tests.TestInfrastructure;
using KUKULCAN.SharedKernel.Database.Tests.TestInfrastructure.internals;

namespace KUKULCAN.SharedKernel.Database.Tests.Abstractions;

[TestFixture]
public sealed class AbstractionsTests
{
    [Test]
    public void ImmutableMarker_ShouldBeImplementable()
    {
        Assert.That(new TestImmutable(), Is.InstanceOf<IImmutable>());
    }

    [Test]
    public void TenantContext_ShouldExposeTenantId()
    {
        var id = Guid.NewGuid();
        ITenantContext context = new TestTenantContext(id);

        Assert.That(context.TenantId, Is.EqualTo(id));
    }

    [Test]
    public void UnitOfWork_ShouldExposeExpectedContract()
    {
        Assert.Multiple(() =>
        {
            Assert.That(typeof(IUnitOfWork).GetMethod(nameof(IUnitOfWork.SaveChangesAsync)), Is.Not.Null);
            Assert.That(typeof(IUnitOfWork).GetMethod(nameof(IUnitOfWork.BeginTransactionAsync)), Is.Not.Null);
            Assert.That(typeof(IUnitOfWork).GetMethod(nameof(IUnitOfWork.CommitTransactionAsync)), Is.Not.Null);
            Assert.That(typeof(IUnitOfWork).GetMethod(nameof(IUnitOfWork.RollbackTransactionAsync)), Is.Not.Null);
            Assert.That(typeof(IUnitOfWork).GetMethod(nameof(IUnitOfWork.EndTransactionAsync)), Is.Not.Null);
            Assert.That(typeof(IUnitOfWork), Is.AssignableTo<IDisposable>());
            Assert.That(typeof(IUnitOfWork), Is.AssignableTo<IAsyncDisposable>());
        });
    }

    private sealed class TestImmutable : IImmutable { }
}
