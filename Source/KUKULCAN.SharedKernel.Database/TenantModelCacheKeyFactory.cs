using Microsoft.EntityFrameworkCore.Infrastructure;

namespace KUKULCAN.SharedKernel.Database;

/// <summary>
/// Extends EF Core's model cache key with the current tenant identifier.
/// </summary>
/// <remarks>
/// Global query filters are part of EF Core's cached model. A filter that closes
/// over a tenant context would otherwise retain the tenant used when the model
/// was first built and could expose no rows, or rows from another tenant, when
/// the same <see cref="DbContext"/> type is subsequently created for a different
/// tenant. Including the tenant identifier in the cache key ensures that each
/// tenant receives a model containing the correct filter value.
/// </remarks>
internal sealed class TenantModelCacheKeyFactory : IModelCacheKeyFactory
{
    /// <inheritdoc />
    public object Create(DbContext context, bool designTime)
    {
        ArgumentNullException.ThrowIfNull(context);

        Guid? tenantId = context is KukulcanDbContextBase kukulcanContext
            ? kukulcanContext.CurrentTenantId
            : null;

        return (context.GetType(), tenantId, designTime);
    }
}
