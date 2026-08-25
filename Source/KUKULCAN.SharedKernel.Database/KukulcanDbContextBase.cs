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
            case DatabaseProvider.MySql:
                ConfigureMySql(optionsBuilder, connStr);
                break;
            default:
                throw new NotSupportedException($"Database provider '{_opts.Provider}' is not supported.");
        }
    }

    private static void ConfigureSqlServer(DbContextOptionsBuilder optionsBuilder, string connectionString, int timeoutSec, int maxRetry, TimeSpan maxDelay)
    {
        try
        {
            Type type = LoadProviderExtensionType(
                "Microsoft.EntityFrameworkCore.SqlServerDbContextOptionsBuilderExtensions",
                "Microsoft.EntityFrameworkCore.SqlServer");
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
            Type type = LoadProviderExtensionType(
                "Microsoft.EntityFrameworkCore.NpgsqlDbContextOptionsBuilderExtensions",
                "Npgsql.EntityFrameworkCore.PostgreSQL");
            InvokeProviderUseMethod(type, "UseNpgsql", optionsBuilder, connectionString, timeoutSec, maxRetry, maxDelay);
        }
        catch (Exception ex) when (ex is not NotSupportedException)
        {
            throw NotInstalled("Npgsql.EntityFrameworkCore.PostgreSQL", ex);
        }
    }

    private static void ConfigureMySql(DbContextOptionsBuilder optionsBuilder, string connectionString)
    {
        try
        {
            Type type = LoadProviderExtensionType(
                "MySQL.Data.EntityFrameworkCore.Extensions.MySQLDbContextOptionsBuilderExtensions",
                "MySql.EntityFrameworkCore");

            MethodInfo? method = type
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == "UseMySQL" && !m.IsGenericMethodDefinition)
                .FirstOrDefault(m =>
                {
                    ParameterInfo[] parameters = m.GetParameters();
                    return parameters.Length == 2
                           && parameters[0].ParameterType == typeof(DbContextOptionsBuilder)
                           && parameters[1].ParameterType == typeof(string);
                });

            if (method is null)
                throw new NotSupportedException("MySql.EntityFrameworkCore does not expose a compatible UseMySQL method.");

            method.Invoke(null, [optionsBuilder, connectionString]);
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

        return assembly.GetType(typeName, throwOnError: false)
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
