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
/// public sealed class CrmDbContext(
///     IOptions&lt;KukulcanDatabaseOptions&gt; options,
///     ITenantContext tenantContext,
///     IClock clock,
///     IDomainEventDispatcher domainEventDispatcher,
///     SlowQueryInterceptor slowQueryInterceptor)
///     : KukulcanDbContextBase(
///         options,
///         tenantContext,
///         clock,
///         domainEventDispatcher,
///         slowQueryInterceptor)
/// {
///     public DbSet&lt;Customer&gt; Customers =&gt; Set&lt;Customer&gt;();
///     public DbSet&lt;Contact&gt; Contacts =&gt; Set&lt;Contact&gt;();
/// }
/// </code>
/// </para>
/// </remarks>
/// <param name="options">Database configuration options.</param>
/// <param name="tenantContext">Current tenant context used by persistence filters.</param>
/// <param name="clock">Clock used by audit and soft-delete interceptors.</param>
/// <param name="domainEventDispatcher">Dispatcher used after successful saves.</param>
/// <param name="slowQueryInterceptor">
/// Optional slow-query interceptor registered by <see cref="Extensions.ServiceCollectionExtensions.AddKukulcanDbContext{TContext}"/>.
/// It is optional to preserve compatibility with contexts created outside dependency injection.
/// </param>
public abstract class KukulcanDbContextBase(
    IOptions<KukulcanDatabaseOptions>? options,
    ITenantContext tenantContext,
    IClock clock,
    IDomainEventDispatcher domainEventDispatcher,
    SlowQueryInterceptor? slowQueryInterceptor = null) : DbContext
{
    private readonly KukulcanDatabaseOptions _opts = options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly ITenantContext _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly IDomainEventDispatcher _domainEventDispatcher = domainEventDispatcher ?? throw new ArgumentNullException(nameof(domainEventDispatcher));
    private readonly SlowQueryInterceptor? _slowQueryInterceptor = slowQueryInterceptor;
    private const string _commandTimeoutMethodName = "CommandTimeout";

    /// <summary>
    /// Gets the current tenant identifier used to build the EF Core model cache key.
    /// </summary>
    internal Guid CurrentTenantId => _tenantContext.TenantId;

    /// <inheritdoc/>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.ReplaceService<IModelCacheKeyFactory, TenantModelCacheKeyFactory>();

        if (_slowQueryInterceptor is not null)
            optionsBuilder.AddInterceptors(_slowQueryInterceptor);

        // Soft-delete must run before audit so that a logical delete is converted
        // to Modified state before AuditSaveChangesInterceptor stamps ModifiedOn.
        optionsBuilder.AddInterceptors(
            new SoftDeleteInterceptor(_clock),
            new AuditSaveChangesInterceptor(_clock),
            new DomainEventDispatchInterceptor(_domainEventDispatcher),
            new ImmutableEntityInterceptor());

        if (optionsBuilder.IsConfigured) return;

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
        var connStr = _opts.ConnectionString;
        var timeout = _opts.CommandTimeoutSeconds;
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

    private static void ConfigureSqlServer(
        DbContextOptionsBuilder optionsBuilder,
        string connectionString,
        int timeoutSec,
        int maxRetry,
        TimeSpan maxDelay)
    {
        try
        {
            Type type = Type.GetType(
                "Microsoft.EntityFrameworkCore.SqlServerDbContextOptionsExtensions, " +
                "Microsoft.EntityFrameworkCore.SqlServer") ?? throw NotInstalled("Microsoft.EntityFrameworkCore.SqlServer");

            InvokeProviderUseMethod(type, "UseSqlServer", optionsBuilder, connectionString, timeoutSec, maxRetry, maxDelay);
        }
        catch (Exception ex) when (ex is not NotSupportedException)
        {
            throw NotInstalled("Microsoft.EntityFrameworkCore.SqlServer", ex);
        }
    }

    private static void ConfigurePostgresSql(
        DbContextOptionsBuilder optionsBuilder,
        string connectionString,
        int timeoutSec,
        int maxRetry,
        TimeSpan maxDelay)
    {
        try
        {
            Type type = Type.GetType(
                "Microsoft.EntityFrameworkCore.NpgsqlDbContextOptionsBuilderExtensions, " +
                "Npgsql.EntityFrameworkCore.PostgreSQL") ?? throw NotInstalled("Npgsql.EntityFrameworkCore.PostgreSQL");

            InvokeProviderUseMethod(type, "UseNpgsql", optionsBuilder, connectionString, timeoutSec, maxRetry, maxDelay);
        }
        catch (Exception ex) when (ex is not NotSupportedException)
        {
            throw NotInstalled("Npgsql.EntityFrameworkCore.PostgreSQL", ex);
        }
    }

    private static void InvokeProviderUseMethod(
        Type extensionType,
        string methodName,
        DbContextOptionsBuilder optionsBuilder,
        string connectionString,
        int timeoutSec,
        int maxRetry,
        TimeSpan maxDelay)
    {
        MethodInfo? method = extensionType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == methodName && !m.IsGenericMethodDefinition)
            .FirstOrDefault(m =>
            {
                ParameterInfo[] parameters = m.GetParameters();
                return parameters.Length == 3
                       && parameters[0].ParameterType == typeof(DbContextOptionsBuilder)
                       && parameters[1].ParameterType == typeof(string)
                       && parameters[2].ParameterType.IsGenericType
                       && parameters[2].ParameterType.GetGenericTypeDefinition() == typeof(Action<>);
            });

        if (method is null)
            throw new NotSupportedException(
                $"Provider '{extensionType.Assembly.GetName().Name}' does not expose a compatible {methodName} method.");

        Type providerOptionsBuilderType = method.GetParameters()[2].ParameterType.GetGenericArguments()[0];

        typeof(KukulcanDbContextBase)
            .GetMethod(nameof(InvokeProviderUseMethodGeneric), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(providerOptionsBuilderType)
            .Invoke(null, [method, optionsBuilder, connectionString, timeoutSec, maxRetry, maxDelay]);
    }

    private static void InvokeProviderUseMethodGeneric<TProviderOptionsBuilder>(
        MethodInfo method,
        DbContextOptionsBuilder optionsBuilder,
        string connectionString,
        int timeoutSec,
        int maxRetry,
        TimeSpan maxDelay)
    {
        Action<TProviderOptionsBuilder> configure = providerOptions =>
        {
            Type providerOptionsType = providerOptions!.GetType();
            providerOptionsType.GetMethod(_commandTimeoutMethodName)?.Invoke(providerOptions, [timeoutSec]);

            if (maxRetry <= 0) return;

            MethodInfo? retryMethod = providerOptionsType
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "EnableRetryOnFailure" && m.GetParameters().Length == 3);

            if (retryMethod is null)
                throw new NotSupportedException(
                    $"Provider '{providerOptionsType.FullName}' does not expose a compatible EnableRetryOnFailure method.");

            retryMethod.Invoke(providerOptions, [maxRetry, maxDelay, null]);
        };

        method.Invoke(null, [optionsBuilder, connectionString, configure]);
    }

    private static NotSupportedException NotInstalled(string package, Exception? inner = null)
        => inner is null
            ? new NotSupportedException(
                $"Package '{package}' is not installed. " +
                $"Add it to the consuming module's Infrastructure project.")
            : new NotSupportedException(
                $"Failed to configure provider. " +
                $"Ensure '{package}' is installed in the consuming project.", inner);

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);
        modelBuilder.ApplySoftDeleteFilter();
        modelBuilder.ApplyTenantFilter(_tenantContext);
    }
}
