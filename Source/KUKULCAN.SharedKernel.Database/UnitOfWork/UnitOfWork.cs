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
    /// <param name="context">Database context used by the unit of work.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is <see langword="null"/>.</exception>
    public UnitOfWork(TContext context)
        => _context = context ?? throw new ArgumentNullException(nameof(context));

    /// <summary>Persists pending changes through the underlying context.</summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The number of state entries written to the database.</returns>
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => _context.SaveChangesAsync(cancellationToken);

    /// <summary>Begins an explicit database transaction.</summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">Thrown when a transaction is already active.</exception>
    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is not null)
            throw new InvalidOperationException("A transaction is already in progress.");

        _transaction = await _context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Saves pending changes and commits the active transaction.</summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">Thrown when no transaction is active.</exception>
    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
            throw new InvalidOperationException("No active transaction to commit.");

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await _transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await _transaction.DisposeAsync().ConfigureAwait(false);
            _transaction = null;
        }
    }

    /// <summary>Rolls back and releases the active transaction.</summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">Thrown when no transaction is active.</exception>
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
            await _transaction.DisposeAsync().ConfigureAwait(false);
            _transaction = null;
        }
    }

    /// <summary>Releases the active transaction without committing or rolling it back.</summary>
    /// <param name="cancellationToken">Token retained for interface compatibility.</param>
    /// <exception cref="InvalidOperationException">Thrown when no transaction is active.</exception>
    public async Task EndTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
            throw new InvalidOperationException("No active transaction to end.");

        await _transaction.DisposeAsync().ConfigureAwait(false);
        _transaction = null;
    }

    /// <summary>Releases the active transaction, if any.</summary>
    public void Dispose()
    {
        _transaction?.Dispose();
        _transaction = null;
    }

    /// <summary>Asynchronously releases the active transaction, if any.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
        {
            await _transaction.DisposeAsync().ConfigureAwait(false);
            _transaction = null;
        }
    }
}
