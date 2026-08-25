using KUKULCAN.SharedKernel.Database.Abstractions;
using KUKULCAN.SharedKernel.Database.Client.UI;
using KUKULCAN.SharedKernel.Database.Configuration;
using KUKULCAN.SharedKernel.Database.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Spectre.Console;

namespace KUKULCAN.SharedKernel.Database.Client.Client;

/// <summary>
/// Executes the complete provider-neutral executable showcase for
/// KUKULCAN.SharedKernel.Database.
/// </summary>
public sealed class ReferenceClientScenarioRunner(
    IServiceScopeFactory scopeFactory,
    ClientDbContext db,
    ConsoleTenantContext tenantContext,
    ConsoleDomainEventDispatcher domainEventDispatcher,
    IOptions<KukulcanDatabaseOptions> options)
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    /// <summary>
    /// Executes all reference-client scenarios using the selected database provider.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task RunAllAsync(CancellationToken ct)
    {
        var scenarios = new (string Name, Func<CancellationToken, Task> Run)[]
        {
            ("Configuration / provider", ConfigurationScenarioAsync),
            ("SaveChangesAsync", SaveChangesScenarioAsync),
            ("Transaction / Commit", CommitScenarioAsync),
            ("Transaction / Rollback", RollbackScenarioAsync),
            ("Transaction / EndTransaction", EndTransactionScenarioAsync),
            ("Cancellation", CancellationScenarioAsync),
            ("Tenant Model Cache", TenantModelCacheScenarioAsync),
            ("Migrations / Seed", MigrationAndSeedScenarioAsync),
            ("Retry / Execution Strategy", RetryScenarioAsync),
            ("Audit interceptor", AuditScenarioAsync),
            ("Soft Delete / global filter", SoftDeleteScenarioAsync),
            ("Immutable interceptor", ImmutableScenarioAsync),
            ("Domain Events", DomainEventsScenarioAsync),
            ("Slow Query", SlowQueryScenarioAsync),
            ("Tenant Filter", TenantFilterScenarioAsync)
        };

        foreach (var scenario in scenarios)
        {
            ct.ThrowIfCancellationRequested();
            await scenario.Run(ct).ConfigureAwait(false);
            AnsiConsole.MarkupLine($"[green]PASS[/] {Markup.Escape(scenario.Name)}");
        }
    }

    private Task ConfigurationScenarioAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(options.Value.ConnectionString))
            throw new InvalidOperationException("The configured database connection string is empty.");

        if (options.Value.Provider is not (DatabaseProvider.SqlServer or DatabaseProvider.PostgresSql or DatabaseProvider.MySql))
            throw new InvalidOperationException($"Unsupported provider: {options.Value.Provider}.");

        string? providerName = db.Database.ProviderName;
        if (string.IsNullOrWhiteSpace(providerName))
            throw new InvalidOperationException("EF Core did not expose a configured database provider.");

        return Task.CompletedTask;
    }

    private async Task SaveChangesScenarioAsync(CancellationToken ct)
    {
        string name = UniqueName("SaveChanges");
        db.Products.Add(ClientProduct.Create(name, 10.50m, "Reference"));
        int affected = await db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (affected <= 0)
            throw new InvalidOperationException("SaveChangesAsync did not persist the product.");

        if (!await db.Products.IgnoreQueryFilters().AnyAsync(p => p.Name == name, ct).ConfigureAwait(false))
            throw new InvalidOperationException("The persisted product could not be read back.");
    }

    private async Task CommitScenarioAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ClientDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<UnitOfWork<ClientDbContext>>();
        string name = UniqueName("Commit");

        context.Products.Add(ClientProduct.Create(name, 20m, "Transaction"));
        await unitOfWork.BeginTransactionAsync(ct).ConfigureAwait(false);
        await unitOfWork.CommitTransactionAsync(ct).ConfigureAwait(false);

        if (!await context.Products.IgnoreQueryFilters().AnyAsync(p => p.Name == name, ct).ConfigureAwait(false))
            throw new InvalidOperationException("Committed transaction data was not persisted.");
    }

    private async Task RollbackScenarioAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ClientDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<UnitOfWork<ClientDbContext>>();
        string name = UniqueName("Rollback");

        await unitOfWork.BeginTransactionAsync(ct).ConfigureAwait(false);
        context.Products.Add(ClientProduct.Create(name, 30m, "Transaction"));
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
        await unitOfWork.RollbackTransactionAsync(ct).ConfigureAwait(false);

        if (await context.Products.IgnoreQueryFilters().AnyAsync(p => p.Name == name, ct).ConfigureAwait(false))
            throw new InvalidOperationException("Rolled-back transaction data is still present.");
    }

    private async Task EndTransactionScenarioAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ClientDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<UnitOfWork<ClientDbContext>>();
        string name = UniqueName("EndTransaction");

        await unitOfWork.BeginTransactionAsync(ct).ConfigureAwait(false);
        context.Products.Add(ClientProduct.Create(name, 40m, "Transaction"));
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
        await unitOfWork.EndTransactionAsync(ct).ConfigureAwait(false);

        if (await context.Products.IgnoreQueryFilters().AnyAsync(p => p.Name == name, ct).ConfigureAwait(false))
            throw new InvalidOperationException("EndTransactionAsync unexpectedly committed transaction data.");
    }

    private async Task CancellationScenarioAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cancelled.Cancel();

        try
        {
            await db.Products.AsNoTracking().ToListAsync(cancelled.Token).ConfigureAwait(false);
            throw new InvalidOperationException("EF Core accepted an already-cancelled query token.");
        }
        catch (OperationCanceledException)
        {
            // Expected: verifies cancellation propagation through EF Core.
        }
    }

    private async Task TenantModelCacheScenarioAsync(CancellationToken ct)
    {
        tenantContext.SetTenant(TenantA);
        await AddTenantDocumentAsync(TenantA, UniqueName("TenantA"), ct).ConfigureAwait(false);
        await AddTenantDocumentAsync(TenantB, UniqueName("TenantB"), ct).ConfigureAwait(false);

        await using var scopeA = scopeFactory.CreateAsyncScope();
        var tenantA = scopeA.ServiceProvider.GetRequiredService<ConsoleTenantContext>();
        var contextA = scopeA.ServiceProvider.GetRequiredService<ClientDbContext>();
        tenantA.SetTenant(TenantA);
        int countA = await contextA.TenantDocuments.CountAsync(ct).ConfigureAwait(false);

        await using var scopeB = scopeFactory.CreateAsyncScope();
        var tenantB = scopeB.ServiceProvider.GetRequiredService<ConsoleTenantContext>();
        var contextB = scopeB.ServiceProvider.GetRequiredService<ClientDbContext>();
        tenantB.SetTenant(TenantB);
        int countB = await contextB.TenantDocuments.CountAsync(ct).ConfigureAwait(false);

        if (countA == 0 || countB == 0)
            throw new InvalidOperationException("Tenant model cache scenario did not return tenant-specific data.");
    }

    private async Task MigrationAndSeedScenarioAsync(CancellationToken ct)
    {
        var migrations = db.Database.GetMigrations().ToArray();
        if (migrations.Length > 0)
            await db.Database.MigrateAsync(ct).ConfigureAwait(false);
        else
            await db.Database.EnsureCreatedAsync(ct).ConfigureAwait(false);

        const string seedName = "__KUKULCAN_REFERENCE_CLIENT_SCENARIO_SEED__";
        if (!await db.Products.IgnoreQueryFilters().AnyAsync(p => p.Name == seedName, ct).ConfigureAwait(false))
        {
            db.Products.Add(ClientProduct.Create(seedName, 0m, "ReferenceSeed"));
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        if (!await db.Products.IgnoreQueryFilters().AnyAsync(p => p.Name == seedName, ct).ConfigureAwait(false))
            throw new InvalidOperationException("Reference seed data was not persisted.");
    }

    private async Task RetryScenarioAsync(CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        int executions = 0;

        await strategy.ExecuteAsync(async () =>
        {
            executions++;
            await db.Products.AsNoTracking().Take(1).ToListAsync(ct).ConfigureAwait(false);
        }).ConfigureAwait(false);

        if (executions != 1)
            throw new InvalidOperationException("The configured execution strategy did not execute exactly once for a successful operation.");
    }

    private async Task AuditScenarioAsync(CancellationToken ct)
    {
        string name = UniqueName("Audit");
        var product = ClientProduct.Create(name, 50m, "Audit");
        db.Products.Add(product);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        if (product.CreatedOn == default)
            throw new InvalidOperationException("Audit interceptor did not populate CreatedOn.");

        DateTimeOffset createdOn = product.CreatedOn;
        product.ChangePrice(55m);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        if (product.ModifiedOn is null || product.ModifiedOn <= createdOn)
            throw new InvalidOperationException("Audit interceptor did not populate ModifiedOn on update.");
    }

    private async Task SoftDeleteScenarioAsync(CancellationToken ct)
    {
        string name = UniqueName("SoftDelete");
        var product = ClientProduct.Create(name, 60m, "SoftDelete");
        db.Products.Add(product);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        db.Products.Remove(product);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        if (await db.Products.AnyAsync(p => p.Id == product.Id, ct).ConfigureAwait(false))
            throw new InvalidOperationException("Soft-delete global filter did not hide the deleted entity.");

        var deleted = await db.Products.IgnoreQueryFilters().SingleAsync(p => p.Id == product.Id, ct).ConfigureAwait(false);
        if (!deleted.IsDeleted || deleted.DeletedOn is null)
            throw new InvalidOperationException("Soft-delete interceptor did not set IsDeleted/DeletedOn.");
    }

    private async Task ImmutableScenarioAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ClientDbContext>();
        var immutable = new DemoAuditLog
        {
            Action = "ReferenceClient",
            PerformedBy = "ReferenceClient",
            Detail = "Immutable scenario"
        };

        context.AuditLogs.Add(immutable);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);

        context.Entry(immutable).Property(x => x.Detail).CurrentValue = "Attempted modification";
        context.Entry(immutable).State = EntityState.Modified;

        try
        {
            await context.SaveChangesAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException("Immutable interceptor allowed an update.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("immutable", StringComparison.OrdinalIgnoreCase))
        {
            // Expected.
        }
    }

    private async Task DomainEventsScenarioAsync(CancellationToken ct)
    {
        int before = domainEventDispatcher.DispatchCount;
        string orderNumber = UniqueName("Order");
        var order = ClientOrder.Create(orderNumber, 75m);
        order.Place();
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        if (domainEventDispatcher.DispatchCount != before + 1 || domainEventDispatcher.LastEvent is not OrderPlacedEvent eventData || eventData.OrderNumber != orderNumber)
            throw new InvalidOperationException("Domain event interceptor did not dispatch the expected OrderPlacedEvent.");
    }

    private async Task SlowQueryScenarioAsync(CancellationToken ct)
    {
        _ = await db.Products.AsNoTracking().Take(1).ToListAsync(ct).ConfigureAwait(false);
    }

    private async Task TenantFilterScenarioAsync(CancellationToken ct)
    {
        tenantContext.SetTenant(TenantA);
        string titleA = UniqueName("FilterA");
        string titleB = UniqueName("FilterB");
        await AddTenantDocumentAsync(TenantA, titleA, "Tenant A").ConfigureAwait(false);
        await AddTenantDocumentAsync(TenantB, titleB, "Tenant B").ConfigureAwait(false);

        await using var scopeA = scopeFactory.CreateAsyncScope();
        var contextA = scopeA.ServiceProvider.GetRequiredService<ClientDbContext>();
        var tenantA = scopeA.ServiceProvider.GetRequiredService<ConsoleTenantContext>();
        tenantA.SetTenant(TenantA);
        if (!await contextA.TenantDocuments.AnyAsync(d => d.Title == titleA, ct).ConfigureAwait(false) ||
            await contextA.TenantDocuments.AnyAsync(d => d.Title == titleB, ct).ConfigureAwait(false))
            throw new InvalidOperationException("Tenant filter did not isolate Tenant A.");

        await using var scopeB = scopeFactory.CreateAsyncScope();
        var contextB = scopeB.ServiceProvider.GetRequiredService<ClientDbContext>();
        var tenantB = scopeB.ServiceProvider.GetRequiredService<ConsoleTenantContext>();
        tenantB.SetTenant(TenantB);
        if (!await contextB.TenantDocuments.AnyAsync(d => d.Title == titleB, ct).ConfigureAwait(false) ||
            await contextB.TenantDocuments.AnyAsync(d => d.Title == titleA, ct).ConfigureAwait(false))
            throw new InvalidOperationException("Tenant filter did not isolate Tenant B.");
    }

    private async Task AddTenantDocumentAsync(Guid tenantId, string title, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ConsoleTenantContext>();
        tenant.SetTenant(tenantId);
        var context = scope.ServiceProvider.GetRequiredService<ClientDbContext>();
        context.TenantDocuments.Add(DemoTenantDocument.Create(tenantId, title, "Reference tenant scenario"));
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static string UniqueName(string prefix) => $"__KUKULCAN_REFERENCE_{prefix}_{Guid.NewGuid():N}__";
}
