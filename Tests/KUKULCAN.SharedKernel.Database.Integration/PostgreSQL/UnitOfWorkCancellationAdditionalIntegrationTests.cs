using KUKULCAN.SharedKernel.Abstractions;
using KUKULCAN.SharedKernel.Database;
using KUKULCAN.SharedKernel.Database.Abstractions;
using KUKULCAN.SharedKernel.Database.Configuration;
using KUKULCAN.SharedKernel.Database.UnitOfWork;
using KUKULCAN.SharedKernel.DomainEvents.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

namespace KUKULCAN.SharedKernel.Database.Integration;

[TestFixture]
[NonParallelizable]
public sealed class UnitOfWorkCancellationAdditionalIntegrationTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private CancellationDbContext _context = null!;

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
    public async Task CommitTransaction_ShouldHonorCancellationTokenAndReleaseRealPostgreSqlTransaction()
    {
        var unitOfWork = new UnitOfWork<CancellationDbContext>(_context);
        await unitOfWork.BeginTransactionAsync();

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(
            async () => await unitOfWork.CommitTransactionAsync(cancellation.Token));

        await unitOfWork.BeginTransactionAsync();
        _context.Entities.Add(new CancellationEntity
        {
            TenantId = _tenantId,
            Name = "After-cancelled-commit"
        });
        await unitOfWork.CommitTransactionAsync();

        Assert.That(
            await _context.Entities.AnyAsync(x => x.Name == "After-cancelled-commit"),
            Is.True);

        await unitOfWork.DisposeAsync();
    }

    [Test]
    public async Task RollbackTransaction_ShouldHonorCancellationTokenAndReleaseRealPostgreSqlTransaction()
    {
        var unitOfWork = new UnitOfWork<CancellationDbContext>(_context);
        await unitOfWork.BeginTransactionAsync();

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(
            async () => await unitOfWork.RollbackTransactionAsync(cancellation.Token));

        await unitOfWork.BeginTransactionAsync();
        _context.Entities.Add(new CancellationEntity
        {
            TenantId = _tenantId,
            Name = "After-cancelled-rollback"
        });
        await unitOfWork.CommitTransactionAsync();

        Assert.That(
            await _context.Entities.AnyAsync(x => x.Name == "After-cancelled-rollback"),
            Is.True);

        await unitOfWork.DisposeAsync();
    }

    [Test]
    public async Task EndTransaction_ShouldReleaseTransactionEvenWhenCancellationTokenIsAlreadyCancelled()
    {
        var unitOfWork = new UnitOfWork<CancellationDbContext>(_context);
        await unitOfWork.BeginTransactionAsync();

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await unitOfWork.EndTransactionAsync(cancellation.Token);

        await unitOfWork.BeginTransactionAsync();
        _context.Entities.Add(new CancellationEntity
        {
            TenantId = _tenantId,
            Name = "After-cancelled-end"
        });
        await unitOfWork.CommitTransactionAsync();

        Assert.That(
            await _context.Entities.AnyAsync(x => x.Name == "After-cancelled-end"),
            Is.True);

        await unitOfWork.DisposeAsync();
    }

    private CancellationDbContext CreateContext()
    {
        var options = Options.Create(new KukulcanDatabaseOptions
        {
            Provider = DatabaseProvider.PostgresSql,
            ConnectionString = IntegrationTestDatabase.ConnectionString,
            CommandTimeoutSeconds = 30,
            Retry = new KukulcanDatabaseOptions.RetryOptions { Enabled = false },
            Pool = new KukulcanDatabaseOptions.PoolOptions { Enabled = false }
        });

        return new CancellationDbContext(
            options,
            new CancellationTenantContext(_tenantId),
            new FixedClock(),
            Mock.Of<IDomainEventDispatcher>());
    }

    private sealed class CancellationDbContext(
        IOptions<KukulcanDatabaseOptions> options,
        ITenantContext tenantContext,
        IClock clock,
        IDomainEventDispatcher dispatcher)
        : KukulcanDbContextBase(options, tenantContext, clock, dispatcher)
    {
        public DbSet<CancellationEntity> Entities => Set<CancellationEntity>();
    }

    private sealed class CancellationEntity : IAuditable
    {
        public int Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTimeOffset CreatedOn { get; set; }
        public DateTimeOffset? ModifiedOn { get; set; }
        public bool IsDeleted { get; set; }
    }

    private sealed class CancellationTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId { get; } = tenantId;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
    }
}
