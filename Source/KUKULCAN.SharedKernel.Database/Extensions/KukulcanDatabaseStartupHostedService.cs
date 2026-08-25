using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace KUKULCAN.SharedKernel.Database.Extensions;

/// <summary>
/// Runs the configured database migration and seed policy during application startup.
/// </summary>
public sealed class KukulcanDatabaseStartupHostedService<TContext>(
    KukulcanDatabaseStartupInitializer<TContext> initializer) : IHostedService
    where TContext : KukulcanDbContextBase
{
    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
        => initializer.InitializeAsync(cancellationToken);

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
