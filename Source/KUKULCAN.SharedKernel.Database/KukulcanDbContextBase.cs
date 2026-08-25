using KUKULCAN.SharedKernel.Database.Configuration;
using KUKULCAN.SharedKernel.Database.Extensions;
using KUKULCAN.SharedKernel.Database.Interceptors;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;

namespace KUKULCAN.SharedKernel.Database;

/// <summary>
/// Abstract base class for all KUKULCAN.SharedKernel.Database module DbContexts.
/// </summary>
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
    private readonly List<IDomainEvent> _pendingDomainEvents = [];
    private readonly HashSet<IDomainEvent> _acknowledgedDomainEvents = [];
    private const string _commandTimeoutMethodName = "CommandTimeout";

    internal Guid CurrentTenantId => _tenantContext.TenantId;

    internal void CapturePendingDomainEvents()
    {
        var events = ChangeTracker.Entries<IHasDomainEvents>()
            .Select(e => e.Entity)
            .SelectMany(e => e.DomainEvents)
            .Where(e => !_acknowledgedDomainEvents.Contains(e) && !_pendingDomainEvents.Contains(e))
            .ToList();

        _pendingDomainEvents.AddRange(events);
    }

    internal async Task DispatchPendingDomainEventsAsync(CancellationToken cancellationToken = default)
    {
        foreach (IDomainEvent domainEvent in _pendingDomainEvents.ToList())
        {
            await _domainEventDispatcher.DispatchAsync(domainEvent, cancellationToken).ConfigureAwait(false);
            _pendingDomainEvents.Remove(domainEvent);
            _acknowledgedDomainEvents.Add(domainEvent);
        }

        if (_pendingDomainEvents.Count != 0)
            return;

        foreach (IHasDomainEvents aggregate in ChangeTracker.Entries<IHasDomainEvents>()
                     .Select(e => e.Entity)
                     .Distinct())
        {
            aggregate.ClearDomainEvents();
        }

        _acknowledgedDomainEvents.Clear();
    }

    internal void DiscardPendingDomainEvents()
    {
        _pendingDomainEvents.Clear();
        _acknowledgedDomainEvents.Clear();
    }

    /// <summary>Configures the EF Core model, interceptors, diagnostics, and database provider.</summary>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.ReplaceService<IModelCacheKeyFactory, TenantModelCacheKeyFactory>();

        if (_slowQueryInterceptor is not null)
            optionsBuilder.AddInterceptors(_slowQueryInterceptor);

        optionsBuilder.AddInterceptors(
            new SoftDeleteInterceptor(_clock),
            new AuditSaveChangesInterceptor(_clock),
            new DomainEventDispatchInterceptor(_domainEventDispatcher),
            new ImmutableEntityInterceptor());

        if (_opts.EnableSensitiveDataLogging)
            optionsBuilder.EnableSensitiveDataLogging();

        if (_opts.EnableDetailedErrors)
            optionsBuilder.EnableDetailedErrors();

        bool databaseProviderConfigured = optionsBuilder.Options.Extensions
            .Any(extension => extension.Info.IsDatabaseProvider);

        if (!databaseProviderConfigured)
            ConfigureProvider(optionsBuilder);
    }

    /// <summary>
    /// Configures the selected EF Core database provider, connection pooling, command timeout, and retry strategy.
    /// </summary>
    protected virtual void ConfigureProvider(DbContextOptionsBuilder optionsBuilder)
    {
        var connStr = _opts.ConnectionString;
        var pool = _opts.Pool;
        var connStringWithPool = BuildProviderConnectionString(_opts.Provider, connStr, pool);
        var timeout = _opts.CommandTimeoutSeconds;
        var maxRetry = _opts.Retry.Enabled ? _opts.Retry.MaxRetryCount : 0;
        var maxDelay = TimeSpan.FromSeconds(_opts.Retry.MaxRetryDelaySeconds);

        switch (_opts.Provider)
        {
            case DatabaseProvider.SqlServer:
                ConfigureSqlServer(optionsBuilder, connStringWithPool, timeout, maxRetry, maxDelay);
                break;
            case DatabaseProvider.PostgresSql:
                ConfigurePostgresSql(optionsBuilder, connStringWithPool, timeout, maxRetry, maxDelay);
                break;
            case DatabaseProvider.MySql:
                ConfigureMySql(optionsBuilder, connStringWithPool, timeout, maxRetry, maxDelay);
                break;
            default:
                throw new NotSupportedException($"Database provider '{_opts.Provider}' is not supported.");
        }
    }

    private static string BuildProviderConnectionString(
        DatabaseProvider provider,
        string connectionString,
        KukulcanDatabaseOptions.PoolOptions pool)
    {
        if (!pool.Enabled)
        {
            return provider switch
            {
                DatabaseProvider.SqlServer => RemoveConnectionStringKeys(connectionString, "Pooling", "Min Pool Size", "Max Pool Size"),
                DatabaseProvider.PostgresSql => RemoveConnectionStringKeys(connectionString, "Pooling", "Minimum Pool Size", "Maximum Pool Size"),
                DatabaseProvider.MySql => RemoveConnectionStringKeys(
                    connectionString,
                    "Pooling",
                    "MinimumPoolSize",
                    "MaximumPoolSize",
                    "MinPoolSize",
                    "MaxPoolSize"),
                _ => connectionString
            };
        }

        return provider switch
        {
            DatabaseProvider.SqlServer => AppendConnectionStringOptions(connectionString,
                $"Pooling=true;Min Pool Size={pool.MinSize};Max Pool Size={pool.MaxSize}"),
            DatabaseProvider.PostgresSql => AppendConnectionStringOptions(connectionString,
                $"Pooling=true;Minimum Pool Size={pool.MinSize};Maximum Pool Size={pool.MaxSize}"),
            DatabaseProvider.MySql => AppendConnectionStringOptions(connectionString,
                $"Pooling=true;MinimumPoolSize={pool.MinSize};MaximumPoolSize={pool.MaxSize}"),
            _ => connectionString
        };
    }

    private static string AppendConnectionStringOptions(string connectionString, string options)
        => string.IsNullOrWhiteSpace(connectionString)
            ? options
            : $"{connectionString.TrimEnd(';')};{options};";

    private static string RemoveConnectionStringKeys(string connectionString, params string[] keys)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return connectionString;

        string[] segments = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(';', segments.Where(segment =>
        {
            int separator = segment.IndexOf('=');
            if (separator <= 0) return true;
            string key = segment[..separator].Trim();
            return !keys.Any(candidate => string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase));
        }));
    }

    private static void ConfigureSqlServer(DbContextOptionsBuilder optionsBuilder, string connectionString, int timeoutSec, int maxRetry, TimeSpan maxDelay)
    {
        try
        {
            Type type = LoadProviderExtensionType("Microsoft.EntityFrameworkCore.SqlServerDbContextOptionsExtensions", "Microsoft.EntityFrameworkCore.SqlServer");
            InvokeProviderUseMethod(type, "UseSqlServer", optionsBuilder, connectionString, timeoutSec, maxRetry, maxDelay);
        }
        catch (Exception ex) when (ex is not NotSupportedException)
        {
            throw NotInstalled("Microsoft.EntityFrameworkCore.SqlServer", ex);
        }
    }

    private static void ConfigurePostgresSql(DbContextOptionsBuilder optionsBuilder, string connectionString, int timeoutSec, int maxRetry, TimeSpan maxDelay)
    {
        try
        {
            Type type = LoadProviderExtensionType("Microsoft.EntityFrameworkCore.NpgsqlDbContextOptionsExtensions", "Npgsql.EntityFrameworkCore.PostgreSQL");
            InvokeProviderUseMethod(type, "UseNpgsql", optionsBuilder, connectionString, timeoutSec, maxRetry, maxDelay);
        }
        catch (Exception ex) when (ex is not NotSupportedException)
        {
            throw NotInstalled("Npgsql.EntityFrameworkCore.PostgreSQL", ex);
        }
    }

    private static void ConfigureMySql(DbContextOptionsBuilder optionsBuilder, string connectionString, int timeoutSec, int maxRetry, TimeSpan maxDelay)
    {
        try
        {
            Type type = LoadProviderExtensionType("Microsoft.EntityFrameworkCore.MySQLDbContextOptionsExtensions", "MySql.EntityFrameworkCore");
            InvokeProviderUseMethod(type, "UseMySQL", optionsBuilder, connectionString, timeoutSec, maxRetry, maxDelay);
        }
        catch (Exception ex) when (ex is not NotSupportedException)
        {
            throw NotInstalled("MySql.EntityFrameworkCore", ex);
        }
    }

    private static Type LoadProviderExtensionType(string typeName, string assemblyName)
    {
        Assembly assembly;
        try
        {
            assembly = Assembly.Load(new AssemblyName(assemblyName));
        }
        catch (FileNotFoundException ex)
        {
            throw NotInstalled(assemblyName, ex);
        }

        Type? type = assembly.GetType(typeName, throwOnError: false);
        if (type is not null)
            return type;

        string shortTypeName = typeName[(typeName.LastIndexOf('.') + 1)..];
        try
        {
            type = assembly.GetTypes()
                .FirstOrDefault(candidate => string.Equals(candidate.Name, shortTypeName, StringComparison.Ordinal));
        }
        catch (ReflectionTypeLoadException ex)
        {
            type = ex.Types.FirstOrDefault(candidate =>
                candidate is not null &&
                string.Equals(candidate.Name, shortTypeName, StringComparison.Ordinal));
        }

        return type
               ?? throw new NotSupportedException($"Assembly '{assemblyName}' does not expose the expected provider extension type '{typeName}'.");
    }

    private static void InvokeProviderUseMethod(Type extensionType, string methodName, DbContextOptionsBuilder optionsBuilder, string connectionString, int timeoutSec, int maxRetry, TimeSpan maxDelay)
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
            throw new NotSupportedException($"Provider '{extensionType.Assembly.GetName().Name}' does not expose a compatible {methodName} method.");

        Type providerOptionsBuilderType = method.GetParameters()[2].ParameterType.GetGenericArguments()[0];

        typeof(KukulcanDbContextBase)
            .GetMethod(nameof(InvokeProviderUseMethodGeneric), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(providerOptionsBuilderType)
            .Invoke(null, [method, optionsBuilder, connectionString, timeoutSec, maxRetry, maxDelay]);
    }

    private static void InvokeProviderUseMethodGeneric<TProviderOptionsBuilder>(MethodInfo method, DbContextOptionsBuilder optionsBuilder, string connectionString, int timeoutSec, int maxRetry, TimeSpan maxDelay)
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
                throw new NotSupportedException($"Provider '{providerOptionsType.FullName}' does not expose a compatible EnableRetryOnFailure method.");

            retryMethod.Invoke(providerOptions, [maxRetry, maxDelay, null]);
        };

        method.Invoke(null, [optionsBuilder, connectionString, configure]);
    }

    private static NotSupportedException NotInstalled(string package, Exception? inner = null)
        => inner is null
            ? new NotSupportedException($"Package '{package}' is not installed. Add it to the consuming module's Infrastructure project.")
            : new NotSupportedException($"Failed to configure provider. Ensure '{package}' is installed in the consuming project.", inner);

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);
        modelBuilder.ApplySoftDeleteFilter();
        modelBuilder.ApplyTenantFilter(_tenantContext);
    }
}
