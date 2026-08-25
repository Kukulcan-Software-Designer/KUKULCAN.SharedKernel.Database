using Microsoft.EntityFrameworkCore.Storage;

namespace KUKULCAN.SharedKernel.Database.UnitOfWork;

/// <summary>
/// Generic unit-of-work implementation backed by a SharedKernel-compatible DbContext.
/// </summary>
public sealed class UnitOfWork<TContext> : IUnitOfWork where TContext : KukulcanDbContextBase
{
    private readonly TContext _context;
    private IDbContextTransaction? _transaction;

    /// <summary>Initializes a unit of work for the specified database context.</summary>
    public UnitOfWork(TContext context)
        => _context = context ?? throw new ArgumentNullException(nameof(context));

    /// <summary>Persists pending changes through the underlying context.</summary>
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => _context.SaveChangesAsync(cancellationToken);

    /// <summary>Begins an explicit database transaction.</summary>
    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is not null)
            throw new InvalidOperationException("A transaction is already in progress.");

        _transaction = await _context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Saves pending changes, commits the active transaction, and then dispatches its domain events.</summary>
    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
            throw new InvalidOperationException("No active transaction to commit.");

        bool committed = false;
        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await _transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            committed = true;
            await _context.DispatchPendingDomainEventsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (!committed)
                _context.DiscardPendingDomainEvents();

            throw;
        }
        finally
        {
            await _transaction.DisposeAsync().ConfigureAwait(false);
            _transaction = null;
        }
    }

    /// <summary>Rolls back and releases the active transaction.</summary>
    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
            throw new InvalidOperationException("No active transaction to roll back.");

        try
        {
            await _transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _context.DiscardPendingDomainEvents();
            await _transaction.DisposeAsync().ConfigureAwait(false);
            _transaction = null;
        }
    }

    /// <summary>Releases the active transaction without committing or rolling it back.</summary>
    public async Task EndTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
            throw new InvalidOperationException("No active transaction to end.");

        try
        {
            await _transaction.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _context.DiscardPendingDomainEvents();
            _transaction = null;
        }
    }

    /// <summary>Releases the active transaction, if any.</summary>
    public void Dispose()
    {
        _transaction?.Dispose();
        _context.DiscardPendingDomainEvents();
        _transaction = null;
    }

    /// <summary>Asynchronously releases the active transaction, if any.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
        {
            await _transaction.DisposeAsync().ConfigureAwait(false);
            _context.DiscardPendingDomainEvents();
            _transaction = null;
        }
    }
}
