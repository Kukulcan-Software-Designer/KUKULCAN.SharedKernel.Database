namespace KUKULCAN.SharedKernel.Database.Abstractions;

/// <summary>
/// Defines the unit-of-work contract for database operations.
/// </summary>
public interface IUnitOfWork : IDisposable, IAsyncDisposable
{
    /// <summary>Persists pending changes.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Begins an explicit database transaction.</summary>
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>Commits the current transaction.</summary>
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>Rolls back the current transaction.</summary>
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>Ends the current transaction and releases its resources.</summary>
    Task EndTransactionAsync(CancellationToken cancellationToken = default);
}
