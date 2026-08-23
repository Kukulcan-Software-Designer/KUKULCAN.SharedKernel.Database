using Microsoft.EntityFrameworkCore.Metadata;

namespace KUKULCAN.SharedKernel.Database.Extensions;

/// <summary>
/// EF Core model conventions used by the database module.
/// </summary>
public static class ModelBuilderExtensions
{
    /// <summary>
    /// Applies a global filter to entities implementing <see cref="ISoftDelete"/>.
    /// </summary>
    /// <param name="modelBuilder">Model builder to configure.</param>
    /// <returns>The same <paramref name="modelBuilder"/> instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="modelBuilder"/> is <see langword="null"/>.</exception>
    public static ModelBuilder ApplySoftDeleteFilter(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        foreach (IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ISoftDelete).IsAssignableFrom(entityType.ClrType)) continue;

            typeof(ModelBuilderExtensions)
                .GetMethod(nameof(SetSoftDeleteFilter), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(entityType.ClrType)
                .Invoke(null, [modelBuilder]);
        }

        return modelBuilder;
    }

    /// <summary>
    /// Applies tenant isolation to entities exposing a <c>TenantId</c> property.
    /// Tenant awareness is intentionally a persistence concern and is not part of SharedKernel.
    /// </summary>
    /// <param name="modelBuilder">Model builder to configure.</param>
    /// <param name="tenantContext">Current tenant context used by the global filter.</param>
    /// <returns>The same <paramref name="modelBuilder"/> instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="modelBuilder"/> or <paramref name="tenantContext"/> is <see langword="null"/>.</exception>
    public static ModelBuilder ApplyTenantFilter(this ModelBuilder modelBuilder, ITenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(tenantContext);

        foreach (IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.IsOwned()) continue;

            // ModelBuilder can expose the entity before convention-based scalar
            // properties have been materialized in its metadata. Inspect the CLR
            // contract first, then explicitly register the property so the query
            // filter is consistently available both on a raw ModelBuilder and on
            // a DbContext model.
            PropertyInfo? tenantProperty = entityType.ClrType.GetProperty(
                "TenantId",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (tenantProperty?.PropertyType != typeof(Guid)) continue;

            modelBuilder.Entity(entityType.ClrType).Property<Guid>("TenantId");

            typeof(ModelBuilderExtensions)
                .GetMethod(nameof(SetTenantFilter), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(entityType.ClrType)
                .Invoke(null, [modelBuilder, tenantContext]);
        }

        return modelBuilder;
    }

    private static void SetSoftDeleteFilter<T>(ModelBuilder modelBuilder)
        where T : class, ISoftDelete
        => modelBuilder.Entity<T>()
            .HasQueryFilter("SoftDeleteFilter", e => !e.IsDeleted);

    private static void SetTenantFilter<T>(ModelBuilder modelBuilder, ITenantContext tenantContext)
        where T : class
        => modelBuilder.Entity<T>()
            .HasQueryFilter("TenantFilter", e => EF.Property<Guid>(e, "TenantId") == tenantContext.TenantId);
}
