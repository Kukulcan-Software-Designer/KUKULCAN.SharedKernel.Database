namespace KUKULCAN.SharedKernel.Database.Tests.UnitOfWork;

[TestFixture]
public sealed class UnitOfWorkTests
{
    [Test]
    public void Constructor_WithNullContext_ShouldThrow()
    {
        Assert.That(
            () => new UnitOfWork<TransactionTestDbContext>(null!),
            Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public async Task BeginTransaction_WhenAlreadyActive_ShouldThrow()
    {
        await using var fixture = TransactionContextFixture.Create();
        var unit = new UnitOfWork<TransactionTestDbContext>(fixture.Context);

        await unit.BeginTransactionAsync();

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await unit.BeginTransactionAsync());

        Assert.That(exception!.Message, Does.Contain("already in progress"));
        await unit.RollbackTransactionAsync();
    }

    [Test]
    public async Task CommitTransaction_WhenInactive_ShouldThrow()
    {
        await using var fixture = TransactionContextFixture.Create();
        var unit = new UnitOfWork<TransactionTestDbContext>(fixture.Context);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await unit.CommitTransactionAsync());

        Assert.That(exception!.Message, Does.Contain("No active transaction"));
    }

    [Test]
    public async Task CommitTransaction_ShouldCompleteAndReleaseTransaction()
    {
        await using var fixture = TransactionContextFixture.Create();
        var unit = new UnitOfWork<TransactionTestDbContext>(fixture.Context);

        await unit.BeginTransactionAsync();
        await unit.CommitTransactionAsync();

        Assert.DoesNotThrowAsync(() => unit.BeginTransactionAsync());
        await unit.RollbackTransactionAsync();
    }

    [Test]
    public async Task CommitTransaction_WhenSaveIsCancelled_ShouldReleaseTransaction()
    {
        await using var fixture = TransactionContextFixture.Create();
        var unit = new UnitOfWork<TransactionTestDbContext>(fixture.Context);
        await unit.BeginTransactionAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAsync(
            Is.InstanceOf<OperationCanceledException>(),
            async () => await unit.CommitTransactionAsync(cancellation.Token));

        Assert.DoesNotThrowAsync(() => unit.BeginTransactionAsync());
        await unit.RollbackTransactionAsync();
    }

    [Test]
    public async Task RollbackTransaction_WhenInactive_ShouldThrow()
    {
        await using var fixture = TransactionContextFixture.Create();
        var unit = new UnitOfWork<TransactionTestDbContext>(fixture.Context);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await unit.RollbackTransactionAsync());

        Assert.That(exception!.Message, Does.Contain("No active transaction"));
    }

    [Test]
    public async Task RollbackTransaction_ShouldCompleteAndReleaseTransaction()
    {
        await using var fixture = TransactionContextFixture.Create();
        var unit = new UnitOfWork<TransactionTestDbContext>(fixture.Context);

        await unit.BeginTransactionAsync();
        await unit.RollbackTransactionAsync();

        Assert.DoesNotThrowAsync(() => unit.BeginTransactionAsync());
        await unit.RollbackTransactionAsync();
    }

    [Test]
    public async Task RollbackTransaction_WhenCancelled_ShouldReleaseTransaction()
    {
        await using var fixture = TransactionContextFixture.Create();
        var unit = new UnitOfWork<TransactionTestDbContext>(fixture.Context);
        await unit.BeginTransactionAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAsync(
            Is.InstanceOf<OperationCanceledException>(),
            async () => await unit.RollbackTransactionAsync(cancellation.Token));

        Assert.DoesNotThrowAsync(() => unit.BeginTransactionAsync());
        await unit.RollbackTransactionAsync();
    }

    [Test]
    public async Task EndTransaction_WhenInactive_ShouldThrow()
    {
        await using var fixture = TransactionContextFixture.Create();
        var unit = new UnitOfWork<TransactionTestDbContext>(fixture.Context);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await unit.EndTransactionAsync());

        Assert.That(exception!.Message, Does.Contain("No active transaction"));
    }

    [Test]
    public async Task EndTransaction_ShouldReleaseTransaction()
    {
        await using var fixture = TransactionContextFixture.Create();
        var unit = new UnitOfWork<TransactionTestDbContext>(fixture.Context);

        await unit.BeginTransactionAsync();
        await unit.EndTransactionAsync();

        Assert.DoesNotThrowAsync(() => unit.BeginTransactionAsync());
        await unit.RollbackTransactionAsync();
    }

    [Test]
    public async Task Dispose_ShouldReleaseActiveTransaction()
    {
        await using var fixture = TransactionContextFixture.Create();
        var unit = new UnitOfWork<TransactionTestDbContext>(fixture.Context);

        await unit.BeginTransactionAsync();
        unit.Dispose();

        Assert.DoesNotThrowAsync(() => unit.BeginTransactionAsync());
        await unit.RollbackTransactionAsync();
    }

    [Test]
    public async Task Dispose_WhenNoTransactionIsActive_ShouldBeIdempotent()
    {
        await using var fixture = TransactionContextFixture.Create();
        var unit = new UnitOfWork<TransactionTestDbContext>(fixture.Context);

        Assert.DoesNotThrow(() => unit.Dispose());
        Assert.DoesNotThrow(() => unit.Dispose());
    }

    [Test]
    public async Task DisposeAsync_ShouldReleaseActiveTransaction()
    {
        await using var fixture = TransactionContextFixture.Create();
        var unit = new UnitOfWork<TransactionTestDbContext>(fixture.Context);

        await unit.BeginTransactionAsync();
        await unit.DisposeAsync();

        Assert.DoesNotThrowAsync(() => unit.BeginTransactionAsync());
        await unit.RollbackTransactionAsync();
    }

    [Test]
    public async Task SaveChangesAsync_ShouldDelegateToContext()
    {
        await using var fixture = TransactionContextFixture.Create();
        var unit = new UnitOfWork<TransactionTestDbContext>(fixture.Context);

        var entity = new TransactionEntity();
        fixture.Context.Entities.Add(entity);

        int result = await unit.SaveChangesAsync();

        Assert.That(result, Is.EqualTo(1));
        Assert.That(await fixture.Context.Entities.CountAsync(), Is.EqualTo(1));
    }
}
