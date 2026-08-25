using KUKULCAN.SharedKernel.Abstractions;
using KUKULCAN.SharedKernel.Database;
using KUKULCAN.SharedKernel.Database.Abstractions;
using KUKULCAN.SharedKernel.Database.Configuration;
using KUKULCAN.SharedKernel.Database.PostgreSQL.Integration;
using KUKULCAN.SharedKernel.Database.UnitOfWork;
using KUKULCAN.SharedKernel.DomainEvents.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

namespace KUKULCAN.SharedKernel.Database.Integration;

[TestFixture]
[NonParallelizable]
public sealed class UnitOfWorkTransactionIntegrationTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private readonly Guid _tenantId = Guid.NewGuid();
    private TransactionDbContext _context = null!;

    [SetUp]
    public async Task SetUp()
    {
        _context = CreateContext();
        await _context.Database.EnsureCreatedAsync();
        await _context.Entities.IgnoreQueryFilters().ExecuteDeleteAsync();
    }

    [TearDown]
    public async Task TearDown()
        => await _context.DisposeAsync();

    [Test]
    public async Task BeginTransaction_ShouldRejectSecondActiveTransactionAgainstRealPostgreSql()
    {
        var unitOfWork = new UnitOfWork<TransactionDbContext>(_context);
        await unitOfWork.BeginTransactionAsync();

        InvalidOperationException exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await unitOfWork.BeginTransactionAsync())!;

        Assert.That(exception.Message, Does.Contain("already in progress"));
        await unitOfWork.RollbackTransactionAsync();
        await unitOfWork.DisposeAsync();
    }

    [Test]
    public async Task CommitTransaction_ShouldRejectMissingTransaction()
    {
        var unitOfWork = new UnitOfWork<TransactionDbContext>(_context);

        InvalidOperationException exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await unitOfWork.CommitTransactionAsync())!;

        Assert.That(exception.Message, Does.Contain("No active transaction"));
        await unitOfWork.DisposeAsync();
    }

    [Test]
    public async Task RollbackTransaction_ShouldRejectMissingTransaction()
    {
        var unitOfWork = new UnitOfWork<TransactionDbContext>(_context);

        InvalidOperationException exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await unitOfWork.RollbackTransactionAsync())!;

        Assert.That(exception.Message, Does.Contain("No active transaction"));
        await unitOfWork.DisposeAsync();
    }

    [Test]
    public async Task EndTransaction_ShouldRejectMissingTransaction()
    {
        var unitOfWork = new UnitOfWork<TransactionDbContext>(_context);

        InvalidOperationException exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await unitOfWork.EndTransactionAsync())!;

        Assert.That(exception.Message, Does.Contain("No active transaction"));
        await unitOfWork.DisposeAsync();
    }

    [Test]
    public async Task EndTransaction_ShouldReleaseRealPostgreSqlTransactionAndAllowAnotherTransaction()
    {
        var unitOfWork = new UnitOfWork<TransactionDbContext>(_context);

        await unitOfWork.BeginTransactionAsync();
        _context.Entities.Add(new TransactionEntity { TenantId = _tenantId, Name = "First" });
        await unitOfWork.SaveChangesAsync();
        await unitOfWork.EndTransactionAsync();

        await unitOfWork.BeginTransactionAsync();
        _context.Entities.Add(new TransactionEntity { TenantId = _tenantId, Name = "Second" });
        await unitOfWork.CommitTransactionAsync();

        Assert.That(
            await _context.Entities.CountAsync(x => x.Name == "Second"),
            Is.EqualTo(1));

        await unitOfWork.DisposeAsync();
    }

    [Test]
    public async Task SaveChanges_ShouldPersistThroughRealPostgreSqlUnitOfWork()
    {
        var unitOfWork = new UnitOfWork<TransactionDbContext>(_context);
        var entity = new TransactionEntity { TenantId = _tenantId, Name = "UnitOfWork-SaveChanges" };

        _context.Entities.Add(entity);
        int saved = await unitOfWork.SaveChangesAsync();

        Assert.That(saved, Is.EqualTo(1));
        Assert.That(entity.Id, Is.GreaterThan(0));
        Assert.That(
            await _context.Entities.AnyAsync(x => x.Id == entity.Id && x.Name == entity.Name),
            Is.True);

        await unitOfWork.DisposeAsync();
    }

    [Test]
    public async Task CommitTransaction_ShouldPersistChangesAfterExplicitSaveChangesAgainstRealPostgreSql()
    {
        var unitOfWork = new UnitOfWork<TransactionDbContext>(_context);
        await unitOfWork.BeginTransactionAsync();

        var entity = new TransactionEntity { TenantId = _tenantId, Name = "Explicit-Save-Then-Commit" };
        _context.Entities.Add(entity);
        int saved = await unitOfWork.SaveChangesAsync();

        Assert.That(saved, Is.EqualTo(1));

        await unitOfWork.CommitTransactionAsync();

        Assert.That(
            await _context.Entities.AnyAsync(x => x.Id == entity.Id && x.Name == entity.Name),
            Is.True);

        await unitOfWork.DisposeAsync();
    }

    [Test]
    public async Task RollbackTransaction_ShouldDiscardPreviouslySavedChangesAgainstRealPostgreSql()
    {
        var unitOfWork = new UnitOfWork<TransactionDbContext>(_context);
        await unitOfWork.BeginTransactionAsync();

        var entity = new TransactionEntity { TenantId = _tenantId, Name = "Should-Rollback" };
        _context.Entities.Add(entity);
        await unitOfWork.SaveChangesAsync();
        Assert.That(entity.Id, Is.GreaterThan(0));

        await unitOfWork.RollbackTransactionAsync();

        Assert.That(
            await _context.Entities.IgnoreQueryFilters().AnyAsync(x => x.Id == entity.Id),
            Is.False);

        await unitOfWork.DisposeAsync();
    }

    [Test]
    public async Task Dispose_ShouldReleaseActiveRealPostgreSqlTransactionAndAllowAnotherTransaction()
    {
        var unitOfWork = new UnitOfWork<TransactionDbContext>(_context);
        await unitOfWork.BeginTransactionAsync();

        _context.Entities.Add(new TransactionEntity { TenantId = _tenantId, Name = "Disposed-Transaction" });
        await unitOfWork.SaveChangesAsync();
        unitOfWork.Dispose();

        await unitOfWork.BeginTransactionAsync();
        _context.Entities.Add(new TransactionEntity { TenantId = _tenantId, Name = "After-Dispose" });
        await unitOfWork.CommitTransactionAsync();

        Assert.That(
            await _context.Entities.AnyAsync(x => x.Name == "After-Dispose"),
            Is.True);
        Assert.That(
            await _context.Entities.IgnoreQueryFilters().AnyAsync(x => x.Name == "Disposed-Transaction"),
            Is.False);

        await unitOfWork.DisposeAsync();
    }

    [Test]
    public async Task DisposeAsync_ShouldReleaseActiveRealPostgreSqlTransactionAndAllowAnotherTransaction()
    {
        var unitOfWork = new UnitOfWork<TransactionDbContext>(_context);
        await unitOfWork.BeginTransactionAsync();

        _context.Entities.Add(new TransactionEntity { TenantId = _tenantId, Name = "Async-Disposed-Transaction" });
        await unitOfWork.SaveChangesAsync();
        await unitOfWork.DisposeAsync();

        await unitOfWork.BeginTransactionAsync();
        _context.Entities.Add(new TransactionEntity { TenantId = _tenantId, Name = "After-Async-Dispose" });
        await unitOfWork.CommitTransactionAsync();

        Assert.That(
            await _context.Entities.AnyAsync(x => x.Name == "After-Async-Dispose"),
            Is.True);
        Assert.That(
            await _context.Entities.IgnoreQueryFilters().AnyAsync(x => x.Name == "Async-Disposed-Transaction"),
            Is.False);

        await unitOfWork.DisposeAsync();
    }

    [Test]
    public async Task UnitOfWork_ShouldSupportMultipleConsecutiveRealPostgreSqlTransactions()
    {
        var unitOfWork = new UnitOfWork<TransactionDbContext>(_context);

        await unitOfWork.BeginTransactionAsync();
        _context.Entities.Add(new TransactionEntity { TenantId = _tenantId, Name = "Transaction-1" });
        await unitOfWork.CommitTransactionAsync();

        await unitOfWork.BeginTransactionAsync();
        _context.Entities.Add(new TransactionEntity { TenantId = _tenantId, Name = "Transaction-2" });
        await unitOfWork.CommitTransactionAsync();

        await unitOfWork.BeginTransactionAsync();
        _context.Entities.Add(new TransactionEntity { TenantId = _tenantId, Name = "Transaction-3" });
        await unitOfWork.CommitTransactionAsync();

        Assert.That(
            await _context.Entities.CountAsync(x => x.Name.StartsWith("Transaction-")),
            Is.EqualTo(3));

        await unitOfWork.DisposeAsync();
    }

    private TransactionDbContext CreateContext()
    {
        var options = Options.Create(new KukulcanDatabaseOptions
        {
            Provider = DatabaseProvider.PostgresSql,
            ConnectionString = IntegrationTestDatabase.ConnectionString,
            CommandTimeoutSeconds = 30,
            Retry = new KukulcanDatabaseOptions.RetryOptions { Enabled = false },
            Pool = new KukulcanDatabaseOptions.PoolOptions { Enabled = false }
        });

        return new TransactionDbContext(
            options,
            new TransactionTenantContext(_tenantId),
            new FixedClock(FixedNow),
            Mock.Of<IDomainEventDispatcher>());
    }

    private sealed class TransactionDbContext(
        IOptions<KukulcanDatabaseOptions> options,
        ITenantContext tenantContext,
        IClock clock,
        IDomainEventDispatcher dispatcher)
        : KukulcanDbContextBase(options, tenantContext, clock, dispatcher)
    {
        public DbSet<TransactionEntity> Entities => Set<TransactionEntity>();
    }

    private sealed class TransactionEntity : IAuditable
    {
        public int Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTimeOffset CreatedOn { get; set; }
        public DateTimeOffset? ModifiedOn { get; set; }
        public bool IsDeleted { get; set; }
    }

    private sealed class TransactionTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId { get; } = tenantId;
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
