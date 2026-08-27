using Microsoft.EntityFrameworkCore;

namespace PartiallyLoadableProvider;

/// <summary>
/// SQL Server provider fixture used to exercise the provider-configuration exception path.
/// The assembly identity is intentionally Microsoft.EntityFrameworkCore.SqlServer while the
/// namespace remains different so production code has to resolve the extension by short name.
/// </summary>
public static class SqlServerDbContextOptionsExtensions
{
    /// <summary>
    /// Mimics the SQL Server provider entry point and invokes the provider configuration callback.
    /// A null options builder fails before the callback, while an invalid timeout fails inside it.
    /// </summary>
    public static void UseSqlServer(
        DbContextOptionsBuilder optionsBuilder,
        string connectionString,
        Action<SqlServerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(configure);

        configure(new SqlServerOptions());
    }

    /// <summary>
    /// Minimal provider-options type required by the reflective provider invocation.
    /// </summary>
    public sealed class SqlServerOptions
    {
        /// <summary>
        /// Provides the command-timeout member expected by the production reflection logic.
        /// </summary>
        public void CommandTimeout(int timeout)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(timeout);
        }
    }
}
