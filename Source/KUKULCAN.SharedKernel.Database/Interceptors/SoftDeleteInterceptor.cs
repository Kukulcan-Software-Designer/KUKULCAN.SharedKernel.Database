using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace KUKULCAN.SharedKernel.Database.Interceptors;

/// <summary>
/// Converts physical deletes of <see cref="ISoftDelete"/> entities into logical deletes.
/// </summary>
/// <param name="clock">Clock used to timestamp logical deletions.</param>
public sealed class SoftDeleteInterceptor(IClock clock) : SaveChangesInterceptor
{
    /// <inheritdoc/>
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ConvertDeletes(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc/>
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ConvertDeletes(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ConvertDeletes(DbContext? context)
    {
        if (context is null) return;

        DateTimeOffset now = clock.UtcNow;

        foreach (EntityEntry<ISoftDelete> entry in context.ChangeTracker.Entries<ISoftDelete>()
                     .Where(e => e.State == EntityState.Deleted))
        {
            entry.State = EntityState.Modified;
            entry.Property(nameof(ISoftDelete.IsDeleted)).CurrentValue = true;
            entry.Property(nameof(ISoftDelete.DeletedOn)).CurrentValue = now;
        }
    }
}
