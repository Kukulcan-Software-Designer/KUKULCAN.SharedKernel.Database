namespace KUKULCAN.SharedKernel.Database.Configuration;

/// <summary>
/// Identifies the relational database engine used by an KUKULCAN module's DbContext.
/// The provider is configured via the <c>Kukulcan:Database:Provider</c> key in
/// <c>appsettings.json</c> and read by <see cref="KukulcanDatabaseOptions"/>.
/// </summary>
/// <remarks>
/// Provider-specific NuGet packages must be added to the consuming project:
/// <list type="table">
///   <listheader><term>Value</term><description>Required package</description></listheader>
///   <item><term>SqlServer</term> <description>Microsoft.EntityFrameworkCore.SqlServer</description></item>
///   <item><term>PostgresSql</term><description>Npgsql.EntityFrameworkCore.PostgreSQL</description></item>
///   <item><term>MySql</term><description>Pomelo.EntityFrameworkCore.MySql</description></item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // app-settings.json
/// {
///   "Kukulcan": {
///     "Database": { "Provider": "MySql" }
///   }
/// }
/// </code>
/// </example>
public enum DatabaseProvider
{
    /// <summary>
    /// Microsoft SQL Server 2019+.
    /// Uses <c>UseSqlServer()</c> with native resilience strategy.
    /// Recommended for Azure and Windows environments.
    /// </summary>
    SqlServer = 0,

    /// <summary>
    /// PostgresSql 14+ via the Npgsql provider.
    /// Uses <c>UseNpgsql()</c> with native resilience strategy.
    /// Recommended for Linux / cloud-neutral deployments.
    /// </summary>
    PostgresSql = 1,

    /// <summary>
    /// MySQL 8+ via the Pomelo Entity Framework Core provider.
    /// </summary>
    MySql = 2,
}
