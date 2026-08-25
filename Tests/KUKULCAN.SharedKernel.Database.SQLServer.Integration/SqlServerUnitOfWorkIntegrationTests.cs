namespace KUKULCAN.SharedKernel.Database.SQLServer.Integration;

[TestFixture]
[NonParallelizable]
public sealed class SqlServerUnitOfWorkIntegrationTests
{
    private SqlServerIntegrationDbContext _context = null!;
    private Guid _tenantId;

    [SetUp]
    public async Task SetUp()
    {
        _tenantId = Guid.NewGuid();
        _context = await SqlServerIntegrationContextFactory.CreateAsync(_tenantId);
        await _context.Entities.IgnoreQueryFilters().ExecuteDeleteAsync();
    }

    [TearDown]
    public async Task TearDown() => await _context.DisposeAsync();

    [Test]
    public async Task UnitOfWork_Commit_ShouldPersistTransaction()
    {
        await using var unit = new UnitOfWork<SqlServerIntegrationDbContext>(_context);
        await unit.BeginTransactionAsync();
        _context.Entities.Add(new SqlServerIntegrationEntity { TenantId = _tenantId, Name = "Committed" });
        await unit.CommitTransactionAsync();
        Assert.That(await _context.Entities.AnyAsync(x => x.Name == "Committed"), Is.True);
    }

    [Test]
    public async Task UnitOfWork_Rollback_ShouldNotPersistTransaction()
    {
        await using var unit = new UnitOfWork<SqlServerIntegrationDbContext>(_context);
        await unit.BeginTransactionAsync();
        _context.Entities.Add(new SqlServerIntegrationEntity { TenantId = _tenantId, Name = "Rolled back" });
        await unit.RollbackTransactionAsync();
        _context.ChangeTracker.Clear();
        Assert.That(await _context.Entities.AnyAsync(x => x.Name == "Rolled back"), Is.False);
    }

    [Test]
    public async Task UnitOfWork_FailedCommit_ShouldRollbackDatabaseTransactionAfterSqlServerConstraintViolation()
    {
        await using var unit = new UnitOfWork<SqlServerIntegrationDbContext>(_context);
        await unit.BeginTransactionAsync();
        var entity = new SqlServerIntegrationEntity { TenantId = _tenantId, Name = "Constraint violation" };
        _context.Entities.Add(entity);
        await unit.SaveChangesAsync();

        entity.Name = null!;

        OperationCanceledException? cancellation = null;
        Exception? caughtException = null;
        try
        {
            await unit.CommitTransactionAsync();
        }
        catch (OperationCanceledException exception)
        {
            cancellation = exception;
        }
        catch (Exception exception)
        {
            caughtException = exception;
        }

        Assert.That(cancellation, Is.Null);
        Assert.That(caughtException, Is.Not.Null);
        _context.ChangeTracker.Clear();
        Assert.That(await _context.Entities.CountAsync(x => x.Name == "Constraint violation"), Is.EqualTo(0));
    }

    [Test]
    public async Task CommitTransaction_ShouldPersistChangesAfterExplicitSaveChangesAgainstRealSqlServer()
    {
        await using var unit = new UnitOfWork<SqlServerIntegrationDbContext>(_context);
        await unit.BeginTransactionAsync();
        _context.Entities.Add(new SqlServerIntegrationEntity { TenantId = _tenantId, Name = "Explicit save" });
        await unit.SaveChangesAsync();
        await unit.CommitTransactionAsync();
        Assert.That(await _context.Entities.AnyAsync(x => x.Name == "Explicit save"), Is.True);
    }

    [Test]
    public async Task SaveChanges_ShouldPersistThroughRealSqlServerUnitOfWork()
    {
        await using var unit = new UnitOfWork<SqlServerIntegrationDbContext>(_context);
        _context.Entities.Add(new SqlServerIntegrationEntity { TenantId = _tenantId, Name = "UnitOfWork save" });
        Assert.That(await unit.SaveChangesAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task BeginTransaction_ShouldRejectSecondActiveTransactionAgainstRealSqlServer()
    {
        await using var unit = new UnitOfWork<SqlServerIntegrationDbContext>(_context);
        await unit.BeginTransactionAsync();
        Assert.ThrowsAsync<InvalidOperationException>(async () => await unit.BeginTransactionAsync());
        await unit.RollbackTransactionAsync();
    }

    [Test]
    public async Task CommitTransaction_ShouldHonorCancellationTokenAndReleaseRealSqlServerTransaction()
    {
        await using var unit = new UnitOfWork<SqlServerIntegrationDbContext>(_context);
        await unit.BeginTransactionAsync();
        await unit.CommitTransactionAsync(CancellationToken.None);
        await unit.BeginTransactionAsync();
        await unit.RollbackTransactionAsync();
    }

    [Test]
    public async Task RollbackTransaction_ShouldHonorCancellationTokenAndReleaseRealSqlServerTransaction()
    {
        await using var unit = new UnitOfWork<SqlServerIntegrationDbContext>(_context);
        await unit.BeginTransactionAsync();
        await unit.RollbackTransactionAsync(CancellationToken.None);
        await unit.BeginTransactionAsync();
        await unit.RollbackTransactionAsync();
    }

    [Test]
    public async Task EndTransaction_ShouldReleaseTransactionEvenWhenCancellationTokenIsAlreadyCancelled()
    {
        await using var unit = new UnitOfWork<SqlServerIntegrationDbContext>(_context);
        await unit.BeginTransactionAsync();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await unit.EndTransactionAsync(cts.Token);
        await unit.BeginTransactionAsync();
        await unit.RollbackTransactionAsync();
    }

    [Test]
    public async Task EndTransaction_ShouldReleaseRealSqlServerTransactionAndAllowAnotherTransaction()
    {
        await using var unit = new UnitOfWork<SqlServerIntegrationDbContext>(_context);
        await unit.BeginTransactionAsync();
        await unit.EndTransactionAsync();
        await unit.BeginTransactionAsync();
        await unit.RollbackTransactionAsync();
    }

    [Test]
    public async Task EndTransaction_ShouldDiscardSavedChangesAgainstRealSqlServer()
    {
        await using var unit = new UnitOfWork<SqlServerIntegrationDbContext>(_context);
        await unit.BeginTransactionAsync();
        _context.Entities.Add(new SqlServerIntegrationEntity { TenantId = _tenantId, Name = "End" });
        await unit.SaveChangesAsync();
        await unit.EndTransactionAsync();
        _context.ChangeTracker.Clear();
        Assert.That(await _context.Entities.AnyAsync(x => x.Name == "End"), Is.False);
    }

    [Test]
    public void CommitTransaction_ShouldRejectMissingTransaction()
    {
        using var unit = new UnitOfWork<SqlServerIntegrationDbContext>(_context);
        Assert.ThrowsAsync<InvalidOperationException>(async () => await unit.CommitTransactionAsync());
    }

    [Test]
    public void RollbackTransaction_ShouldRejectMissingTransaction()
    {
        using var unit = new UnitOfWork<SqlServerIntegrationDbContext>(_context);
        Assert.ThrowsAsync<InvalidOperationException>(async () => await unit.RollbackTransactionAsync());
    }

    [Test]
    public void EndTransaction_ShouldRejectMissingTransaction()
    {
        using var unit = new UnitOfWork<SqlServerIntegrationDbContext>(_context);
        Assert.ThrowsAsync<InvalidOperationException>(async () => await unit.EndTransactionAsync());
    }

    [Test]
    public async Task Dispose_ShouldReleaseActiveRealSqlServerTransactionAndAllowAnotherTransaction()
    {
        var unit = new UnitOfWork<SqlServerIntegrationDbContext>(_context);
        await unit.BeginTransactionAsync();
        unit.Dispose();
        await unit.BeginTransactionAsync();
        await unit.RollbackTransactionAsync();
    }

    [Test]
    public async Task DisposeAsync_ShouldReleaseActiveRealSqlServerTransactionAndAllowAnotherTransaction()
    {
        var unit = new UnitOfWork<SqlServerIntegrationDbContext>(_context);
        await unit.BeginTransactionAsync();
        await unit.DisposeAsync();
        await unit.BeginTransactionAsync();
        await unit.RollbackTransactionAsync();
    }

    [Test]
    public async Task Dispose_ShouldReleaseTransactionAndDiscardSavedChangesAgainstRealSqlServer()
    {
        var unit = new UnitOfWork<SqlServerIntegrationDbContext>(_context);
        await unit.BeginTransactionAsync();
        _context.Entities.Add(new SqlServerIntegrationEntity { TenantId = _tenantId, Name = "Dispose" });
        await unit.SaveChangesAsync();
        unit.Dispose();
        _context.ChangeTracker.Clear();
        Assert.That(await _context.Entities.AnyAsync(x => x.Name == "Dispose"), Is.False);
    }

    [Test]
    public async Task DisposeAsync_ShouldBeIdempotentWithoutActiveTransactionAgainstRealSqlServer()
    {
        var unit = new UnitOfWork<SqlServerIntegrationDbContext>(_context);
        await unit.DisposeAsync();
        await unit.DisposeAsync();
    }

    [Test]
    public async Task RollbackTransaction_ShouldDiscardPreviouslySavedChangesAgainstRealSqlServer()
    {
        await using var unit = new UnitOfWork<SqlServerIntegrationDbContext>(_context);
        await unit.BeginTransactionAsync();
        _context.Entities.Add(new SqlServerIntegrationEntity { TenantId = _tenantId, Name = "Previously saved" });
        await unit.SaveChangesAsync();
        await unit.RollbackTransactionAsync();
        _context.ChangeTracker.Clear();
        Assert.That(await _context.Entities.AnyAsync(x => x.Name == "Previously saved"), Is.False);
    }

    [Test]
    public async Task UnitOfWork_ShouldSupportMultipleConsecutiveRealSqlServerTransactions()
    {
        await using var unit = new UnitOfWork<SqlServerIntegrationDbContext>(_context);
        await unit.BeginTransactionAsync();
        _context.Entities.Add(new SqlServerIntegrationEntity { TenantId = _tenantId, Name = "Tx1" });
        await unit.CommitTransactionAsync();
        await unit.BeginTransactionAsync();
        _context.Entities.Add(new SqlServerIntegrationEntity { TenantId = _tenantId, Name = "Tx2" });
        await unit.CommitTransactionAsync();
        Assert.That(await _context.Entities.CountAsync(x => x.Name == "Tx1" || x.Name == "Tx2"), Is.EqualTo(2));
    }
}
