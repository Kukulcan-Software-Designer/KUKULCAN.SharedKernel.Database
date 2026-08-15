using KUKULCAN.SharedKernel.Database.Configuration;
using KUKULCAN.SharedKernel.Database.Interceptors;
using KUKULCAN.SharedKernel.Database.UnitOfWork;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KUKULCAN.SharedKernel.Database.Extensions;

/// <summary>
/// Extension methods for <see cref="IServiceCollection"/> that register an KUKULCAN
/// module's DbContext together with all required infrastructure services.
/// </summary>
/// <example>
/// <code>
/// // In a module's Infrastructure DependencyInjection.cs:
/// public static IServiceCollection AddCrmInfrastructure(
///     this IServiceCollection services,
///     IConfiguration          configuration)
/// {
///     services.AddKukulcanDbContext&lt;CrmDbContext&gt;(configuration);
///     services.AddScoped&lt;ICustomerRepository, CustomerRepository&gt;();
///     return services;
/// }
/// </code>
/// </example>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a module's <typeparamref name="TContext"/> together with
    /// <see cref="KukulcanDatabaseOptions"/>, <see cref="IUnitOfWork"/>, and
    /// all required database infrastructure services.
    /// </summary>
    /// <typeparam name="TContext">
    /// The module's DbContext type. Must inherit from <see cref="KukulcanDbContextBase"/>.
    /// </typeparam>
    /// <param name="services">The DI service collection.</param>
    /// <param name="configuration">
    /// The application configuration. The <c>Kukulcan:Database</c> section
    /// (<see cref="KukulcanDatabaseOptions.SectionKey"/>) is read to configure the
    /// provider, connection string, and all options.
    /// </param>
    /// <returns>The <paramref name="services"/> for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <c>Kukulcan:Database:ConnectionString</c> is missing or empty.
    /// </exception>
    /// <remarks>
    /// This single call registers:
    /// <list type="bullet">
    ///   <item><see cref="KukulcanDatabaseOptions"/> via <c>IOptions&lt;KukulcanDatabaseOptions&gt;</c></item>
    ///   <item><typeparamref name="TContext"/> (scoped)</item>
    ///   <item><see cref="IUnitOfWork"/> → <see cref="UnitOfWork{TContext}"/> (scoped)</item>
    ///   <item><see cref="SlowQueryInterceptor"/> (singleton)</item>
    /// </list>
    /// </remarks>
    public static IServiceCollection AddKukulcanDbContext<TContext>(this IServiceCollection services,
        IConfiguration configuration) where TContext : KukulcanDbContextBase
    {
        // ① Bind and validate options
        IConfigurationSection section = configuration.GetSection(KukulcanDatabaseOptions.SectionKey);
        services.Configure<KukulcanDatabaseOptions>(section);

        KukulcanDatabaseOptions opts = section.Get<KukulcanDatabaseOptions>() ?? new KukulcanDatabaseOptions();

        if (string.IsNullOrWhiteSpace(opts.ConnectionString))
            throw new InvalidOperationException(
                $"Missing required configuration: {KukulcanDatabaseOptions.SectionKey}:ConnectionString. " +
                $"Ensure it is set in app-settings.json or environment variables.");

        // ② Register the DbContext (scoped — one per HTTP request / unit of work)
        services.AddDbContext<TContext>();

        // ③ Register IUnitOfWork backed by this specific module's context
        services.AddScoped<IUnitOfWork, UnitOfWork<TContext>>();

        // ④ SlowQueryInterceptor as singleton (stateless — only logs)
        services.AddSingleton<SlowQueryInterceptor>();

        return services;
    }
}
