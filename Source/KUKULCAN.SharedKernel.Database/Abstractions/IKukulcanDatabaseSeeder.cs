namespace KUKULCAN.SharedKernel.Database.Abstractions;

/// <summary>
/// Defines application-owned seed data for a KUKULCAN database context.
/// </summary>
/// <typeparam name="TContext">The database context type being seeded.</typeparam>
public interface IKukulcanDatabaseSeeder<in TContext>
    where TContext : KukulcanDbContextBase
{
    /// <summary>
    /// Seeds application-owned data into the database.
    /// </summary>
    /// <param name="context">The context used to write seed data.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    Task SeedAsync(TContext context, CancellationToken cancellationToken = default);
}
