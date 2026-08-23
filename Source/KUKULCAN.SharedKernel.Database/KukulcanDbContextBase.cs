using KUKULCAN.SharedKernel.Database.Configuration;
using KUKULCAN.SharedKernel.Database.Extensions;
using KUKULCAN.SharedKernel.Database.Interceptors;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;

namespace KUKULCAN.SharedKernel.Database;

/// <summary>
/// Abstract base class for all KUKULCAN.SharedKernel.Database module DbContexts.
/// Centralizes every cross-cutting persistence concern so that individual module
/// DbContexts only need to declare their own <c>DbSet&lt;T&gt;</c> properties and
/// set their schema in <c>OnModelCreating</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Responsibilities handled by this base class:</b>
/// <list type="bullet">
///   <item>Database provider selection based on <see cref="KukulcanDatabaseOptions.Provider"/>.</item>
///   <item>Auto-discovery of all <c>IEntityTypeConfiguration&lt;T&gt;</c> in the calling module's assembly.</item>
///   <item>Global soft-delete query filter (<c>WHERE IsDeleted = false</c>) for <see cref="ISoftDelete"/> entities.</item>
///   <item>Global tenant isolation filter (<c>WHERE TenantId = @current</c>) for entities exposing a <c>TenantId</c> property.</item>
///   <item>Audit field population via <see cref="AuditSaveChangesInterceptor"/>.</item>
///   <item>Soft-delete conversion via <see cref="SoftDeleteInterceptor"/>.</item>
///   <item>Domain event dispatch via <see cref="DomainEventDispatchInterceptor"/>.</item>
///   <item>Immutable entity enforcement via <see cref="ImmutableEntityInterceptor"/>.</item>
///   <item>Slow query logging via <see cref="SlowQueryInterceptor"/>.</item>
/// </list>
/// </para>
/// <para>
/// <b>How to create a module DbContext:</b>
/// <code>
/// public sealed class CrmDbContext : KukulcanDbContextBase
/// {
///     public CrmDbContext(
///         IOptions&lt;KukulcanDatabaseOptions&gt; options,
///         ITenantContext      tenantContext,
///         IClock              clock,
///         IDomainEventDispatcher domainEventDispatcher)
///         : base(options, tenantContext, clock, domainEventDispatcher)
///     { }
///
///     public DbSet&lt;Customer&gt; Customers =&gt; Set&lt;Customer&gt;();
///     public DbSet&lt;Contact&gt;  Contacts  =&gt; Set&lt;Contact&gt;();
///
///     protected override void OnModelCreating(ModelBuilder mBuilder)
///     {
///         mBuilder.HasDefaultSchema("crm");
///         base.OnModelCreating(mBuilder);   // runs auto-discovery + filters
///     }
/// }
/// </code>
/// </para>
/// </remarks>
/// <remarks>
/// Initializes a new instance with all required cross-cutting services.
/// </remarks>
/// <param name="options">Database configuration options.</param>
/// <param name="tenantContext">Current tenant context used by persistence filters.</param>
/// <param name="clock">Clock used by audit and soft-delete interceptors.</param>
/// <param name="domainEventDispatcher">Dispatcher used after successful saves.</param>
public abstract class KukulcanDbContextBase(IOptions<KukulcanDatabaseOptions>? options,
    ITenantContext tenantContext, IClock clock, IDomainEventDispatcher domainEventDispatcher) : DbContext
{
    private readonly KukulcanDatabaseOptions _opts = options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly ITenantContext _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly IDomainEventDispatcher _domainEventDispatcher = domainEventDispatcher ?? throw new ArgumentNullException(nameof(domainEventDispatcher));
    private const string _commandTimeoutMethodName = "CommandTimeout";

    /// <summary>
    /// Gets the current tenant identifier used to build the EF Core model cache key.
    /// </summary>
    internal Guid CurrentTenantId => _tenantContext.TenantId;

    // ── OnConfiguring — provider selection ────────────────────────────────────

    /// <inheritdoc/>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // EF Core caches the model by DbContext type. Tenant filters are tenant-specific,
        // therefore the tenant must participate in the model cache key even when the
        // consuming context has already configured its database provider.
        optionsBuilder.ReplaceService<IModelCacheKeyFactory, TenantModelCacheKeyFactory>();

        if (optionsBuilder.IsConfigured) return;

        // Register all five interceptors
        optionsBuilder.AddInterceptors(
            new AuditSaveChangesInterceptor(_clock),
            new SoftDeleteInterceptor(_clock),
            new DomainEventDispatchInterceptor(_domainEventDispatcher),
            new ImmutableEntityInterceptor());

        if (_opts.EnableSensitiveDataLogging)
            optionsBuilder.EnableSensitiveDataLogging();

        if (_opts.EnableDetailedErrors)
            optionsBuilder.EnableDetailedErrors();

        ConfigureProvider(optionsBuilder);
    }

    /// <summary>
    /// Configures the database provider based on <see cref="KukulcanDatabaseOptions.Provider"/>.
    /// Override in a derived class to customize provider configuration.
    /// </summary>
    /// <param name="optionsBuilder">EF Core options builder to configure.</param>
    protected virtual void ConfigureProvider(DbContextOptionsBuilder optionsBuilder)
    {
        var connStr  = _opts.ConnectionString;
        var timeout  = _opts.CommandTimeoutSeconds;
        var maxRetry = _opts.Retry.Enabled ? _opts.Retry.MaxRetryCount : 0;
        var maxDelay = TimeSpan.FromSeconds(_opts.Retry.MaxRetryDelaySeconds);

        switch (_opts.Provider)
        {
            case DatabaseProvider.SqlServer:
                ConfigureSqlServer(optionsBuilder, connStr, timeout, maxRetry, maxDelay);
                break;
            case DatabaseProvider.PostgresSql:
                ConfigurePostgresSql(optionsBuilder, connStr, timeout, maxRetry, maxDelay);
                break;
            default:
                throw new NotSupportedException(
                    $"Database provider '{_opts.Provider}' is not supported.");
        }
    }

    // ── Provider-specific configuration ───────────────────────────────────────
    // Each method uses reflection to avoid direct package references in this library.
    // A clear error is thrown when the required NuGet package is missing.

    private static void ConfigureSqlServer(DbContextOptionsBuilder optionsBuilder,
        string connectionString, int timeoutSec, int maxRetry, TimeSpan maxDelay)
    {
        try
        {
            Type type = Type.GetType(
                "Microsoft.EntityFrameworkCore.SqlServerDbContextOptionsExtensions, " +
                "Microsoft.EntityFrameworkCore.SqlServer") ?? throw NotInstalled("Microsoft.EntityFrameworkCore.SqlServer");

            type.GetMethod("UseSqlServer",
                    [typeof(DbContextOptionsBuilder), typeof(string), typeof(Action<object>)])
                ?.Invoke(null, [optionsBuilder, connectionString,
                    (Action<object>)(o =>
                    {
                        Type t = o.GetType();
                        t.GetMethod(_commandTimeoutMethodName)?.Invoke(o, [timeoutSec]);
                        if (maxRetry > 0)
                            t.GetMethod("EnableRetryOnFailure",
                                    [typeof(int), typeof(TimeSpan), typeof(IEnumerable<int>)])
                                ?.Invoke(o, [maxRetry, maxDelay, null]);
                    })]);
        }
        catch (Exception ex) when (ex is not NotSupportedException)
        {
            throw NotInstalled("Microsoft.EntityFrameworkCore.SqlServer", ex);
        }
    }

    private static void ConfigurePostgresSql(DbContextOptionsBuilder optionsBuilder,
        string connectionString, int timeoutSec, int maxRetry, TimeSpan maxDelay)
    {
        try
        {
            var type = Type.GetType(
                "Microsoft.EntityFrameworkCore.NpgsqlDbContextOptionsBuilderExtensions, " +
                "Npgsql.EntityFrameworkCore.PostgresSQL") ?? throw NotInstalled("Npgsql.EntityFrameworkCore.PostgresSQL");

            type.GetMethod("UseNpgsql",
                    [typeof(DbContextOptionsBuilder), typeof(string), typeof(Action<object>)])
                ?.Invoke(null, [optionsBuilder, connectionString,
                    (Action<object>)(o =>
                    {
                        var t = o.GetType();
                        t.GetMethod(_commandTimeoutMethodName)?.Invoke(o, [timeoutSec]);
                        if (maxRetry > 0)
                            t.GetMethod("EnableRetryOnFailure",
                                    [typeof(int), typeof(TimeSpan), typeof(IEnumerable<string>)])
                                ?.Invoke(o, [maxRetry, maxDelay, null]);
                    })]);
        }
        catch (Exception ex) when (ex is not NotSupportedException)
        {
            throw NotInstalled("Npgsql.EntityFrameworkCore.PostgresSQL", ex);
        }
    }

    private static NotSupportedException NotInstalled(string package, Exception? inner = null)
        => inner is null
            ? new NotSupportedException(
                $"Package '{package}' is not installed. " +
                $"Add it to the consuming module's Infrastructure project.")
            : new NotSupportedException(
                $"Failed to configure provider. " +
                $"Ensure '{package}' is installed in the consuming project.", inner);

    // ── OnModelCreating ────────────────────────────────────────────────────────

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Auto-discover all IEntityTypeConfiguration<T> in the derived module's assembly
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);

        // Global soft-delete filter: WHERE IsDeleted = false
        modelBuilder.ApplySoftDeleteFilter();

        // Global tenant isolation filter: WHERE TenantId = @currentTenantId
        modelBuilder.ApplyTenantFilter(_tenantContext);
    }
}
