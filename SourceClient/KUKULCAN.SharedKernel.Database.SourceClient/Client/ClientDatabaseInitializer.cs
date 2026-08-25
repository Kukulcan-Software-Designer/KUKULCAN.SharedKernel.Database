using Microsoft.EntityFrameworkCore;

namespace KUKULCAN.SharedKernel.Database.Client.Client;

/// <summary>
/// Initializes the reference-client database without making assumptions about the selected provider.
/// Existing migrations are applied when available; otherwise EF Core creates the demo schema.
/// </summary>
public sealed class ClientDatabaseInitializer(ClientDbContext db)
{
    /// <summary>
    /// Applies the database schema strategy and inserts the deterministic reference seed data.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for database operations.</param>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var migrations = await db.Database.GetMigrationsAsync(cancellationToken);

        if (migrations.Any())
        {
            await db.Database.MigrateAsync(cancellationToken);
        }
        else
        {
            await db.Database.EnsureCreatedAsync(cancellationToken);
        }

        await SeedAsync(cancellationToken);
    }

    private async Task SeedAsync(CancellationToken cancellationToken)
    {
        const string seedName = "__KUKULCAN_REFERENCE_CLIENT_SEED__";

        if (await db.Products.IgnoreQueryFilters().AnyAsync(p => p.Name == seedName, cancellationToken))
            return;

        db.Products.Add(ClientProduct.Create(seedName, 0m, "ReferenceClientSeed"));
        await db.SaveChangesAsync(cancellationToken);
    }
}
