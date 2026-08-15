namespace KUKULCAN.SharedKernel.Database.Abstractions;

/// <summary>
/// Provides the tenant identifier used by persistence-level tenant isolation.
/// </summary>
public interface ITenantContext
{
    /// <summary>
    /// Gets the current tenant identifier.
    /// </summary>
    Guid TenantId { get; }
}
