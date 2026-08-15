using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace KUKULCAN.SharedKernel.Database.Interceptors;

/// <summary>
/// Populates the <see cref="IAuditable"/> timestamps before EF Core saves changes.
/// </summary>
/// <param name="clock">Clock used to populate audit timestamps.</param>
public sealed class AuditSaveChangesInterceptor(IClock clock) : SaveChangesInterceptor
{
    /// <inheritdoc/>
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        UpdateAuditFields(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc/>
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        UpdateAuditFields(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void UpdateAuditFields(DbContext? context)
    {
        if (context is null) return;

        DateTimeOffset now = clock.UtcNow;

        foreach (EntityEntry<IAuditable> entry in context.ChangeTracker.Entries<IAuditable>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Property(nameof(IAuditable.CreatedOn)).CurrentValue = now;
                    break;
                case EntityState.Modified:
                    entry.Property(nameof(IAuditable.ModifiedOn)).CurrentValue = now;
                    break;
            }
        }
    }
}
