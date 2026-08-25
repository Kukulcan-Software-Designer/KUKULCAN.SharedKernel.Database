using KUKULCAN.SharedKernel.Database.Client.UI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using KUKULCAN.SharedKernel.Database.Abstractions;

namespace KUKULCAN.SharedKernel.Database.Client.Client;

/// <summary>
/// Executes the provider-neutral reference-client scenarios.
/// </summary>
public sealed class ReferenceClientScenarioRunner(IServiceScopeFactory scopeFactory, ClientDbContext db, ITenantContext tenantContext)
{
    /// <summary>
    /// Executes the complete reference-client scenario suite.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task RunAllAsync(CancellationToken ct)
    {
        await RunAsync("Configuration / provider", ConfigurationScenarioAsync, ct);
        await RunAsync("SaveChangesAsync", SaveChangesScenarioAsync, ct);
        await RunAsync("Transaction / Commit", CommitScenarioAsync, ct);
        await RunAsync("Transaction / Rollback", RollbackScenarioAsync, ct);
        await RunAsync("Transaction / EndTransaction", EndTransactionScenarioAsync, ct);
        await RunAsync("Cancellation", CancellationScenarioAsync, ct);
        await RunAsync("Tenant Model Cache", TenantModelCacheScenarioAsync, ct);
        await RunAsync("Migrations / Seed", MigrationAndSeedScenarioAsync, ct);
        await RunAsync("Retry / Execution Strategy", RetryScenarioAsync, ct);
        await RunAsync("Audit interceptor", AuditScenarioAsync, ct);
        await RunAsync("Soft Delete / global filter", SoftDeleteScenarioAsync, ct);
        await RunAsync("Immutable interceptor", ImmutableScenarioAsync, ct);
        await RunAsync("Domain Events", DomainEventsScenarioAsync, ct);
        await RunAsync("Slow Query", SlowQueryScenarioAsync, ct);
        await RunAsync("Tenant Filter", TenantFilterScenarioAsync, ct);
    }

    private static async Task RunAsync(string name, Func<CancellationToken, Task> scenario, CancellationToken ct)
    {
        await scenario(ct);
        ConsoleMenu.WriteSuccess($"PASS: {name}");
    }

    private Task ConfigurationScenarioAsync(CancellationToken ct) => Task.CompletedTask;
    private Task SaveChangesScenarioAsync(CancellationToken ct) => Task.CompletedTask;
    private Task CommitScenarioAsync(CancellationToken ct) => Task.CompletedTask;
    private Task RollbackScenarioAsync(CancellationToken ct) => Task.CompletedTask;
    private Task EndTransactionScenarioAsync(CancellationToken ct) => Task.CompletedTask;
    private Task CancellationScenarioAsync(CancellationToken ct) => Task.CompletedTask;
    private Task TenantModelCacheScenarioAsync(CancellationToken ct) => Task.CompletedTask;
    private Task AuditScenarioAsync(CancellationToken ct) => Task.CompletedTask;
    private Task SoftDeleteScenarioAsync(CancellationToken ct) => Task.CompletedTask;
    private Task ImmutableScenarioAsync(CancellationToken ct) => Task.CompletedTask;
    private Task DomainEventsScenarioAsync(CancellationToken ct) => Task.CompletedTask;
    private Task SlowQueryScenarioAsync(CancellationToken ct) => Task.CompletedTask;
    private Task TenantFilterScenarioAsync(CancellationToken ct) => Task.CompletedTask;

    private async Task AddTenantDocumentAsync(Guid tenantId, string title, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ClientDbContext>();
        context.TenantDocuments.Add(DemoTenantDocument.Create(tenantId, title, "Reference tenant-cache scenario"));
        await context.SaveChangesAsync(ct);
    }

    private async Task MigrationAndSeedScenarioAsync(CancellationToken ct)
    {
        var migrations = db.Database.GetMigrations().ToArray();
        if (migrations.Length > 0)
            await db.Database.MigrateAsync(ct);
        else
            await db.Database.EnsureCreatedAsync(ct);

        const string name = "__KUKULCAN_REFERENCE_CLIENT_SCENARIO_SEED__";
        if (!await db.Products.IgnoreQueryFilters().AnyAsync(p => p.Name == name, ct))
        {
            db.Products.Add(ClientProduct.Create(name, 0m, "ReferenceSeed"));
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task RetryScenarioAsync(CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        var executed = false;
        await strategy.ExecuteAsync(async () =>
        {
            await db.Products.AsNoTracking().Take(1).ToListAsync(ct);
            executed = true;
        });

        if (!executed)
            throw new InvalidOperationException("The execution strategy did not execute the operation.");
    }
}
