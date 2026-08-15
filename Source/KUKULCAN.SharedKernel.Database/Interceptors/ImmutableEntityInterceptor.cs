namespace KUKULCAN.SharedKernel.Database.Interceptors;

/// <summary>
/// Prevents updates and deletes of entities marked with the database persistence
/// contract <see cref="IImmutable"/>.
/// </summary>
public sealed class ImmutableEntityInterceptor : SaveChangesInterceptor
{
    /// <inheritdoc/>
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ThrowIfImmutableEntityModified(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc/>
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ThrowIfImmutableEntityModified(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void ThrowIfImmutableEntityModified(DbContext? context)
    {
        if (context is null) return;

        string[] violations = context.ChangeTracker
            .Entries<IImmutable>()
            .Where(e => e.State is EntityState.Modified or EntityState.Deleted)
            .Select(e => e.Entity.GetType().Name)
            .ToArray();

        if (violations.Length == 0) return;

        throw new InvalidOperationException(
            "Attempt to modify or delete immutable entity/entities: " +
            $"{string.Join(", ", violations)}. Entities implementing IImmutable are append-only and cannot be updated or deleted after insertion.");
    }
}
