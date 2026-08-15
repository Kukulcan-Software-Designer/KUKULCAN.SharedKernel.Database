namespace KUKULCAN.SharedKernel.Database.Interceptors;

/// <summary>
/// Dispatches pending SharedKernel domain events after a successful save operation.
/// </summary>
/// <param name="dispatcher">Dispatcher used to publish pending domain events.</param>
public sealed class DomainEventDispatchInterceptor(IDomainEventDispatcher dispatcher) : SaveChangesInterceptor
{
    /// <inheritdoc/>
    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        await DispatchDomainEventsAsync(eventData.Context, cancellationToken).ConfigureAwait(false);
        return await base.SavedChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        DispatchDomainEventsAsync(eventData.Context, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        return base.SavedChanges(eventData, result);
    }

    private async Task DispatchDomainEventsAsync(DbContext? context, CancellationToken cancellationToken)
    {
        if (context is null) return;

        List<IHasDomainEvents> aggregates = context.ChangeTracker
            .Entries<IHasDomainEvents>()
            .Select(e => e.Entity)
            .Where(e => e.DomainEvents.Count > 0)
            .ToList();

        List<IDomainEvent> events = aggregates
            .SelectMany(a => a.DomainEvents)
            .ToList();

        foreach (IHasDomainEvents aggregate in aggregates)
            aggregate.ClearDomainEvents();

        foreach (IDomainEvent domainEvent in events)
            await dispatcher.DispatchAsync(domainEvent, cancellationToken).ConfigureAwait(false);
    }
}
