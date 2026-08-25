using KUKULCAN.SharedKernel.Database.Configuration;
using KUKULCAN.SharedKernel.Database.Interceptors;
using KUKULCAN.SharedKernel.Database.UnitOfWork;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KUKULCAN.SharedKernel.Database.Extensions;

/// <summary>
/// Extension methods for registering the KUKULCAN database infrastructure.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a module's database context together with options, unit of work,
    /// interceptors, and the configured startup migration/seed policy.
    /// </summary>
    public static IServiceCollection AddKukulcanDbContext<TContext>(
        this IServiceCollection services,
        IConfiguration configuration)
        where TContext : KukulcanDbContextBase
    {
        IConfigurationSection section = configuration.GetSection(KukulcanDatabaseOptions.SectionKey);
        services.Configure<KukulcanDatabaseOptions>(section);

        KukulcanDatabaseOptions opts = section.Get<KukulcanDatabaseOptions>() ?? new KukulcanDatabaseOptions();

        if (string.IsNullOrWhiteSpace(opts.ConnectionString))
            throw new InvalidOperationException(
                $"Missing required configuration: {KukulcanDatabaseOptions.SectionKey}:ConnectionString. " +
                "Ensure it is set in appsettings.json or environment variables.");

        services.AddSingleton<SlowQueryInterceptor>();
        services.AddDbContext<TContext>((serviceProvider, optionsBuilder) =>
            optionsBuilder.AddInterceptors(
                serviceProvider.GetRequiredService<SlowQueryInterceptor>()));

        services.AddScoped<IUnitOfWork, UnitOfWork<TContext>>();
        services.AddScoped<KukulcanDatabaseStartupInitializer<TContext>>();
        services.AddHostedService<KukulcanDatabaseStartupHostedService<TContext>>();

        return services;
    }
}
