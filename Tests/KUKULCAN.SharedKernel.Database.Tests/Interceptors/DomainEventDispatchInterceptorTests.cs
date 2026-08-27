using System.Reflection;
using KUKULCAN.SharedKernel.Database.Tests.TestInfrastructure;
using KUKULCAN.SharedKernel.Database.Tests.TestInfrastructure.internals;

namespace KUKULCAN.SharedKernel.Database.Tests.Interceptors;

[TestFixture]
public sealed class DomainEventDispatchInterceptorTests
{
    [Test]
    public void Constructor_WithNullDispatcher_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => new DomainEventDispatchInterceptor(null!));
    }

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
    public async Task SavedChangesAsync_WhenSecondEventFails_ShouldNotRedispatchFirstEventOnRetry()
    {
        var result = DatabaseTestContextFactory.Create();
        await using var context = result.Context;
        var aggregate = new DomainEventEntityForTests();
        var first = new TestDomainEvent();
        var second = new TestDomainEvent();
        var dispatchAttempts = 0;

        aggregate.AddDomainEvent(first);
        aggregate.AddDomainEvent(second);
        context.Add(aggregate);

        result.Dispatcher
            .Setup(x => x.DispatchAsync(It.IsAny<IDomainEvent>(), It.IsAny<CancellationToken>()))
            .Callback<IDomainEvent, CancellationToken>((domainEvent, _) =>
            {
                dispatchAttempts++;
                if (ReferenceEquals(domainEvent, second) && dispatchAttempts == 2)
                    throw new InvalidOperationException("Simulated second-event failure.");
            })
            .Returns(Task.CompletedTask);

        Assert.ThrowsAsync<InvalidOperationException>(
            () => context.SaveChangesAsync());

        await context.SaveChangesAsync();

        Assert.Multiple(() =>
        {
            result.Dispatcher.Verify(
                x => x.DispatchAsync(first, It.IsAny<CancellationToken>()),
                Times.Once);
            result.Dispatcher.Verify(
                x => x.DispatchAsync(second, It.IsAny<CancellationToken>()),
                Times.Exactly(2));
            Assert.That(aggregate.DomainEvents, Is.Empty);
        });
    }

    [Test]
    public async Task SavedChangesAsync_WhenDispatcherCreatesPendingEvent_ShouldLeavePendingEventsForLaterDispatch()
    {
        var result = DatabaseTestContextFactory.Create();
        await using var context = result.Context;
        var aggregate = new DomainEventEntityForTests();
        var first = new TestDomainEvent();
        var second = new TestDomainEvent();
        var secondEventCreated = false;

        aggregate.AddDomainEvent(first);
        context.Add(aggregate);

        result.Dispatcher
            .Setup(x => x.DispatchAsync(It.IsAny<IDomainEvent>(), It.IsAny<CancellationToken>()))
            .Callback<IDomainEvent, CancellationToken>((domainEvent, _) =>
            {
                if (!ReferenceEquals(domainEvent, first) || secondEventCreated)
                    return;

                secondEventCreated = true;
                aggregate.AddDomainEvent(second);
                context.CapturePendingDomainEvents();
            })
            .Returns(Task.CompletedTask);

        context.CapturePendingDomainEvents();
        await context.DispatchPendingDomainEventsAsync();

        Assert.Multiple(() =>
        {
            result.Dispatcher.Verify(
                x => x.DispatchAsync(first, It.IsAny<CancellationToken>()),
                Times.Once);
            result.Dispatcher.Verify(
                x => x.DispatchAsync(second, It.IsAny<CancellationToken>()),
                Times.Never);
            Assert.That(aggregate.DomainEvents, Has.Count.EqualTo(2));
            Assert.That(aggregate.DomainEvents, Does.Contain(first));
            Assert.That(aggregate.DomainEvents, Does.Contain(second));
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
    public async Task CaptureAndDispatchIfCommittedAsync_WithNullContext_ShouldReturnWithoutDispatching()
    {
        var method = typeof(DomainEventDispatchInterceptor).GetMethod(
            "CaptureAndDispatchIfCommittedAsync",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.That(method, Is.Not.Null);

        var task = (Task)method!.Invoke(
            null,
            [null, CancellationToken.None])!;

        Assert.That(task, Is.Not.Null);
        await task;
    }
}
