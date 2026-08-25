using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KUKULCAN.SharedKernel.Database.Extensions;

/// <summary>
/// Applies the configured startup migration and optional application seed policy for a KUKULCAN context.
/// </summary>
public sealed class KukulcanDatabaseStartupInitializer<TContext>(
    IServiceScopeFactory scopeFactory,
    IOptions<KukulcanDatabaseOptions> options)
    where TContext : KukulcanDbContextBase
{
    private readonly KukulcanDatabaseOptions _options = options?.Value
        ?? throw new ArgumentNullException(nameof(options));

    /// <summary>
    /// Initializes the database according to <see cref="KukulcanDatabaseOptions.Migration"/>.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Migration.AutoMigrateOnStartup && !_options.Migration.SeedDataOnStartup)
            return;

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        TContext context = scope.ServiceProvider.GetRequiredService<TContext>();

        if (_options.Migration.AutoMigrateOnStartup)
            await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

        if (_options.Migration.SeedDataOnStartup)
        {
            IKukulcanDatabaseSeeder<TContext>? seeder =
                scope.ServiceProvider.GetService<IKukulcanDatabaseSeeder<TContext>>();

            if (seeder is not null)
                await seeder.SeedAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }
}
