namespace KUKULCAN.SharedKernel.Database.Configuration;

/// <summary>
/// Strongly-typed configuration options for KUKULCAN database connections.
/// Bound to the <c>Kukulcan:Database</c> section in <c>appsettings.json</c>.
/// </summary>
/// <remarks>
/// All modules in a single deployment share the same provider and connection string
/// by default. For deployments where modules use different databases, override the
/// options per-module using named <c>IOptions</c> or environment-specific configuration.
/// </remarks>
/// <example>
/// <code>
/// // app-settings.json
/// {
///   "Kukulcan": {
///     "Database": {
///       "Provider":              "PostgresSql",
///       "ConnectionString":      "Host=localhost;Database=Kukulcan;Username=Kukulcan;Password=Kukulcan_pass;",
///       "CommandTimeoutSeconds": 30,
///       "EnableSensitiveDataLogging": false,
///       "EnableDetailedErrors":  false,
///       "Retry":     { "Enabled": true, "MaxRetryCount": 3, "MaxRetryDelaySeconds": 30 },
///       "Pool":      { "Enabled": true, "MinSize": 5, "MaxSize": 100 },
///       "Migration": { "AutoMigrateOnStartup": false, "SeedDataOnStartup": true }
///     }
///   }
/// }
/// </code>
/// </example>
public sealed class KukulcanDatabaseOptions
{
    /// <summary>Configuration section key: <c>"Kukulcan:Database"</c>.</summary>
    public const string SectionKey = "Kukulcan:Database";

    /// <summary>Gets or sets the database engine provider. Default: <see cref="DatabaseProvider.SqlServer"/>.</summary>
    public DatabaseProvider Provider { get; set; } = DatabaseProvider.SqlServer;

    /// <summary>Gets or sets the ADO.NET connection string.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the command timeout in seconds for all EF Core database commands.
    /// Default: 30 seconds.
    /// </summary>
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets whether parameter values are included in EF Core log output.
    /// Must be <c>false</c> in production. Default: <c>false</c>.
    /// </summary>
    public bool EnableSensitiveDataLogging { get; set; }

    /// <summary>
    /// Gets or sets whether detailed EF Core errors are enabled.
    /// Useful in development; disable in production. Default: <c>false</c>.
    /// </summary>
    public bool EnableDetailedErrors { get; set; }

    /// <summary>Gets retry policy options for transient database failures.</summary>
    public RetryOptions Retry { get; set; } = new();

    /// <summary>Gets connection pool options.</summary>
    public PoolOptions Pool { get; set; } = new();

    /// <summary>Gets EF Core migration and seed data options.</summary>
    public MigrationOptions Migration { get; set; } = new();

    // ── Nested option classes ─────────────────────────────────────────────────

    /// <summary>Retry policy configuration for transient database failures.</summary>
    public sealed class RetryOptions
    {
        /// <summary>Enables automatic retry on transient failures. Default: <c>true</c>.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Maximum retry attempts before the operation fails. Default: 3.</summary>
        public int MaxRetryCount { get; set; } = 3;

        /// <summary>
        /// Maximum delay in seconds between retry attempts (exponential back-off cap).
        /// Default: 30.
        /// </summary>
        public int MaxRetryDelaySeconds { get; set; } = 30;
    }

    /// <summary>Connection pool configuration.</summary>
    public sealed class PoolOptions
    {
        /// <summary>Enables connection pooling. Default: <c>true</c>.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Minimum pool size. Default: 5.</summary>
        public int MinSize { get; set; } = 5;

        /// <summary>Maximum pool size. Default: 100.</summary>
        public int MaxSize { get; set; } = 100;
    }

    /// <summary>EF Core migration and seed data configuration.</summary>
    public sealed class MigrationOptions
    {
        /// <summary>
        /// Applies pending EF Core migrations automatically at application startup.
        /// Recommended value: <c>false</c> in production (use CI/CD scripts instead).
        /// Default: <c>false</c>.
        /// </summary>
        public bool AutoMigrateOnStartup { get; set; } = false;

        /// <summary>
        /// Applies seed data automatically at startup. Default: <c>true</c>.
        /// </summary>
        public bool SeedDataOnStartup { get; set; } = true;
    }
}
