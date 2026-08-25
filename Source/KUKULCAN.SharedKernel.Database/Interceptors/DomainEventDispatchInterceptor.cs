namespace KUKULCAN.SharedKernel.Database.Interceptors;

/// <summary>
/// Captures pending SharedKernel domain events after a successful save operation.
/// Events are dispatched immediately when no explicit transaction is active and are
/// deferred until <see cref="UnitOfWork.UnitOfWork{TContext}"/> commits an explicit transaction.
/// </summary>
/// <param name="dispatcher">Dispatcher used by the owning DbContext when events are published.</param>
public sealed class DomainEventDispatchInterceptor(IDomainEventDispatcher dispatcher) : SaveChangesInterceptor
{
    private readonly IDomainEventDispatcher _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

    /// <inheritdoc/>
    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        await CaptureAndDispatchIfCommittedAsync(eventData.Context, cancellationToken).ConfigureAwait(false);
        return await base.SavedChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        CaptureAndDispatchIfCommittedAsync(eventData.Context, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        return base.SavedChanges(eventData, result);
    }

    private static async Task CaptureAndDispatchIfCommittedAsync(DbContext? context, CancellationToken cancellationToken)
    {
        if (context is not KukulcanDbContextBase kukulcanContext)
            return;

        kukulcanContext.CapturePendingDomainEvents();

        // When EF Core is using an implicit transaction, SavedChanges is reached after
        // the database operation has committed. Explicit transactions are dispatched
        // by UnitOfWork only after CommitAsync succeeds.
        if (context.Database.CurrentTransaction is null)
            await kukulcanContext.DispatchPendingDomainEventsAsync(cancellationToken).ConfigureAwait(false);
    }
}
