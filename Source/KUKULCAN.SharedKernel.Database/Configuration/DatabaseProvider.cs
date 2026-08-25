namespace KUKULCAN.SharedKernel.Database.Configuration;

/// <summary>
/// Identifies the relational database engine used by an KUKULCAN module's DbContext.
/// </summary>
/// <remarks>
/// Provider-specific NuGet packages must be added to the consuming project:
/// <list type="table">
///   <listheader><term>Value</term><description>Required package</description></listheader>
///   <item><term>SqlServer</term><description>Microsoft.EntityFrameworkCore.SqlServer</description></item>
///   <item><term>PostgresSql</term><description>Npgsql.EntityFrameworkCore.PostgreSQL</description></item>
///   <item><term>MySql</term><description>MySql.EntityFrameworkCore</description></item>
/// </list>
/// </remarks>
public enum DatabaseProvider
{
    /// <summary>
    /// Microsoft SQL Server 2019+.
    /// </summary>
    SqlServer = 0,

    /// <summary>
    /// PostgreSQL 14+ via the Npgsql provider.
    /// </summary>
    PostgresSql = 1,

    /// <summary>
    /// MySQL 8+ via the official MySQL Connector/NET Entity Framework Core provider.
    /// </summary>
    MySql = 2,
}
