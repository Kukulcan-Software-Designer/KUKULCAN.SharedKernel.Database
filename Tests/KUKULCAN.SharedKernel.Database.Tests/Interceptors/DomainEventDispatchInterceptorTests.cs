using KUKULCAN.SharedKernel.Database.Tests.TestInfrastructure;
using KUKULCAN.SharedKernel.Database.Tests.TestInfrastructure.internals;

namespace KUKULCAN.SharedKernel.Database.Tests.Interceptors;

[TestFixture]
public sealed class DomainEventDispatchInterceptorTests
{
    [Test]
    public async Task SavedChangesAsync_ShouldDispatchEventsAndClearAggregate()
    {
        var result = DatabaseTestContextFactory.Create();
        await using var context = result.Context;
        var aggregate = new DomainEventEntityForTests();
        var first = new TestDomainEvent();
        var second = new TestDomainEvent();

        aggregate.AddDomainEvent(first);
        aggregate.AddDomainEvent(second);
        context.Add(aggregate);

        await context.SaveChangesAsync();

        Assert.Multiple(() =>
        {
            Assert.That(aggregate.DomainEvents, Is.Empty);
            result.Dispatcher.Verify(
                x => x.DispatchAsync(first, It.IsAny<CancellationToken>()),
                Times.Once);
            result.Dispatcher.Verify(
                x => x.DispatchAsync(second, It.IsAny<CancellationToken>()),
                Times.Once);
        });
    }

    [Test]
    public void SavedChanges_ShouldDispatchEventsAndClearAggregate()
    {
        var result = DatabaseTestContextFactory.Create();
        using var context = result.Context;
        var aggregate = new DomainEventEntityForTests();
        var domainEvent = new TestDomainEvent();

        aggregate.AddDomainEvent(domainEvent);
        context.Add(aggregate);

        context.SaveChanges();

        Assert.Multiple(() =>
        {
            Assert.That(aggregate.DomainEvents, Is.Empty);
            result.Dispatcher.Verify(
                x => x.DispatchAsync(domainEvent, It.IsAny<CancellationToken>()),
                Times.Once);
        });
    }

    [Test]
    public async Task SavedChangesAsync_WithNoEvents_ShouldNotDispatch()
    {
        var result = DatabaseTestContextFactory.Create();
        await using var context = result.Context;

        context.Add(new DomainEventEntityForTests());

        await context.SaveChangesAsync();

        result.Dispatcher.Verify(
            x => x.DispatchAsync(It.IsAny<IDomainEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task DispatchDomainEventsAsync_WithNullContext_ShouldReturnWithoutDispatching()
    {
        var dispatcher = new Mock<IDomainEventDispatcher>();
        var interceptor = new DomainEventDispatchInterceptor(dispatcher.Object);
        var method = typeof(DomainEventDispatchInterceptor).GetMethod(
            "DispatchDomainEventsAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(method, Is.Not.Null);

        var task = (Task)method!.Invoke(
            interceptor,
            [null, CancellationToken.None])!;

        await task;

        dispatcher.Verify(
            x => x.DispatchAsync(It.IsAny<IDomainEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
