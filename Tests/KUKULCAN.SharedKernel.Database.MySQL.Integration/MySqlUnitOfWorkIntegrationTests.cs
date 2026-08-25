namespace KUKULCAN.SharedKernel.Database.Integration.MySQL;

[TestFixture]
[NonParallelizable]
public sealed class MySqlUnitOfWorkIntegrationTests
{
    private MySqlIntegrationDbContext _context = null!;
    private Guid _tenantId;

    [SetUp]
    public async Task SetUp()
    {
        _tenantId = Guid.NewGuid();
        _context = await MySqlIntegrationContextFactory.CreateAsync(_tenantId);
        await _context.Entities.IgnoreQueryFilters().ExecuteDeleteAsync();
    }

    [TearDown]
    public async Task TearDown() => await _context.DisposeAsync();

    [Test]
    public async Task UnitOfWork_Commit_ShouldPersistTransaction()
    {
        await using var unit = new UnitOfWork<MySqlIntegrationDbContext>(_context);
        await unit.BeginTransactionAsync();
        _context.Entities.Add(new MySqlIntegrationEntity { TenantId = _tenantId, Name = "Committed" });
        await unit.CommitTransactionAsync();
        Assert.That(await _context.Entities.AnyAsync(x => x.Name == "Committed"), Is.True);
    }

    [Test]
    public async Task UnitOfWork_Rollback_ShouldNotPersistTransaction()
    {
        await using var unit = new UnitOfWork<MySqlIntegrationDbContext>(_context);
        await unit.BeginTransactionAsync();
        _context.Entities.Add(new MySqlIntegrationEntity { TenantId = _tenantId, Name = "Rolled back" });
        await unit.RollbackTransactionAsync();
        _context.ChangeTracker.Clear();
        Assert.That(await _context.Entities.AnyAsync(x => x.Name == "Rolled back"), Is.False);
    }

    [Test]
    public async Task SaveChanges_ShouldPersistThroughRealMySqlUnitOfWork()
    {
        await using var unit = new UnitOfWork<MySqlIntegrationDbContext>(_context);
        _context.Entities.Add(new MySqlIntegrationEntity { TenantId = _tenantId, Name = "UnitOfWork save" });
        Assert.That(await unit.SaveChangesAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task BeginTransaction_ShouldRejectSecondActiveTransactionAgainstRealMySql()
    {
        await using var unit = new UnitOfWork<MySqlIntegrationDbContext>(_context);
        await unit.BeginTransactionAsync();
        Assert.ThrowsAsync<InvalidOperationException>(async () => await unit.BeginTransactionAsync());
        await unit.RollbackTransactionAsync();
    }

    [Test]
    public async Task EndTransaction_ShouldReleaseRealMySqlTransactionAndAllowAnotherTransaction()
    {
        await using var unit = new UnitOfWork<MySqlIntegrationDbContext>(_context);
        await unit.BeginTransactionAsync();
        await unit.EndTransactionAsync();
        await unit.BeginTransactionAsync();
        await unit.RollbackTransactionAsync();
    }

    [Test]
    public async Task EndTransaction_ShouldDiscardSavedChangesAgainstRealMySql()
    {
        await using var unit = new UnitOfWork<MySqlIntegrationDbContext>(_context);
        await unit.BeginTransactionAsync();
        _context.Entities.Add(new MySqlIntegrationEntity { TenantId = _tenantId, Name = "End" });
        await unit.SaveChangesAsync();
        await unit.EndTransactionAsync();
        _context.ChangeTracker.Clear();
        Assert.That(await _context.Entities.AnyAsync(x => x.Name == "End"), Is.False);
    }

    [Test]
    public void CommitTransaction_ShouldRejectMissingTransaction()
    {
        using var unit = new UnitOfWork<MySqlIntegrationDbContext>(_context);
        Assert.ThrowsAsync<InvalidOperationException>(async () => await unit.CommitTransactionAsync());
    }

    [Test]
    public void RollbackTransaction_ShouldRejectMissingTransaction()
    {
        using var unit = new UnitOfWork<MySqlIntegrationDbContext>(_context);
        Assert.ThrowsAsync<InvalidOperationException>(async () => await unit.RollbackTransactionAsync());
    }

    [Test]
    public async Task DisposeAsync_ShouldReleaseActiveRealMySqlTransactionAndAllowAnotherTransaction()
    {
        var unit = new UnitOfWork<MySqlIntegrationDbContext>(_context);
        await unit.BeginTransactionAsync();
        await unit.DisposeAsync();
        await unit.BeginTransactionAsync();
        await unit.RollbackTransactionAsync();
    }

    [Test]
    public async Task RollbackTransaction_ShouldDiscardPreviouslySavedChangesAgainstRealMySql()
    {
        await using var unit = new UnitOfWork<MySqlIntegrationDbContext>(_context);
        await unit.BeginTransactionAsync();
        _context.Entities.Add(new MySqlIntegrationEntity { TenantId = _tenantId, Name = "Previously saved" });
        await unit.SaveChangesAsync();
        await unit.RollbackTransactionAsync();
        _context.ChangeTracker.Clear();
        Assert.That(await _context.Entities.AnyAsync(x => x.Name == "Previously saved"), Is.False);
    }

    [Test]
    public async Task UnitOfWork_ShouldSupportMultipleConsecutiveRealMySqlTransactions()
    {
        await using var unit = new UnitOfWork<MySqlIntegrationDbContext>(_context);
        await unit.BeginTransactionAsync();
        _context.Entities.Add(new MySqlIntegrationEntity { TenantId = _tenantId, Name = "Tx1" });
        await unit.CommitTransactionAsync();
        await unit.BeginTransactionAsync();
        _context.Entities.Add(new MySqlIntegrationEntity { TenantId = _tenantId, Name = "Tx2" });
        await unit.CommitTransactionAsync();
        Assert.That(await _context.Entities.CountAsync(x => x.Name == "Tx1" || x.Name == "Tx2"), Is.EqualTo(2));
    }
}
