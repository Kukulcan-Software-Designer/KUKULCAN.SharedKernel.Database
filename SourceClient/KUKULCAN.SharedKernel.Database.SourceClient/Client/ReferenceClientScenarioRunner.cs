using KUKULCAN.SharedKernel.Database.Abstractions;
using KUKULCAN.SharedKernel.Database.Client.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KUKULCAN.SharedKernel.Database.Client;

/// <summary>
/// Executes the provider-neutral reference scenarios exposed by the database client.
/// </summary>
public sealed class ReferenceClientScenarioRunner(
    IServiceScopeFactory scopeFactory,
    ConsoleTenantContext tenantContext,
    ConsoleDomainEventDispatcher domainEventDispatcher)
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    /// <summary>Runs all reference scenarios.</summary>
    public async Task RunAllAsync(CancellationToken ct = default)
    {
        await RunScenarioAsync("Configuration / provider", ConfigurationScenarioAsync, ct).ConfigureAwait(false);
        await RunScenarioAsync("SaveChangesAsync", SaveChangesScenarioAsync, ct).ConfigureAwait(false);
        await RunScenarioAsync("Transaction / Commit", TransactionCommitScenarioAsync, ct).ConfigureAwait(false);
        await RunScenarioAsync("Transaction / Rollback", TransactionRollbackScenarioAsync, ct).ConfigureAwait(false);
        await RunScenarioAsync("Transaction / EndTransaction", EndTransactionScenarioAsync, ct).ConfigureAwait(false);
        await RunScenarioAsync("Cancellation", CancellationScenarioAsync, ct).ConfigureAwait(false);
        await RunScenarioAsync("Tenant Model Cache", TenantModelCacheScenarioAsync, ct).ConfigureAwait(false);
        await RunScenarioAsync("Migrations / Seed", MigrationSeedScenarioAsync, ct).ConfigureAwait(false);
        await RunScenarioAsync("Retry / Execution Strategy", RetryScenarioAsync, ct).ConfigureAwait(false);
        await RunScenarioAsync("Audit interceptor", AuditScenarioAsync, ct).ConfigureAwait(false);
        await RunScenarioAsync("Soft Delete / global filter", SoftDeleteScenarioAsync, ct).ConfigureAwait(false);
        await RunScenarioAsync("Immutable interceptor", ImmutableScenarioAsync, ct).ConfigureAwait(false);
        await RunScenarioAsync("Domain Events", DomainEventScenarioAsync, ct).ConfigureAwait(false);
        await RunScenarioAsync("Slow Query", SlowQueryScenarioAsync, ct).ConfigureAwait(false);
        await RunScenarioAsync("Tenant Filter", TenantFilterScenarioAsync, ct).ConfigureAwait(false);
    }

    private static async Task RunScenarioAsync(
        string name,
        Func<CancellationToken, Task> scenario,
        CancellationToken ct)
    {
        await scenario(ct).ConfigureAwait(false);
        AnsiConsole.MarkupLine($"[green]✔ PASS[/] {name}");
    }

    private Task ConfigurationScenarioAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var scope = scopeFactory.CreateScope();
        _ = scope.ServiceProvider.GetRequiredService<ClientDbContext>();
        return Task.CompletedTask;
    }

    private async Task SaveChangesScenarioAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClientDbContext>();

        var product = ClientProduct.Create(UniqueName("Save"), 10m, "Reference");
        db.Products.Add(product);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        if (!await db.Products.IgnoreQueryFilters().AnyAsync(p => p.Id == product.Id, ct).ConfigureAwait(false))
            throw new InvalidOperationException("SaveChangesAsync did not persist the product.");
    }

    private async Task TransactionCommitScenarioAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var db = provider.GetRequiredService<ClientDbContext>();
        var uow = provider.GetRequiredService<IUnitOfWork>();

        await uow.BeginTransactionAsync(ct).ConfigureAwait(false);

        var product = ClientProduct.Create(UniqueName("Commit"), 20m, "Reference");
        db.Products.Add(product);
        await uow.CommitTransactionAsync(ct).ConfigureAwait(false);

        await using var verificationScope = scopeFactory.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<ClientDbContext>();
        if (!await verificationDb.Products.IgnoreQueryFilters().AnyAsync(p => p.Id == product.Id, ct).ConfigureAwait(false))
            throw new InvalidOperationException("Committed transaction was not persisted.");
    }

    private async Task TransactionRollbackScenarioAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var db = provider.GetRequiredService<ClientDbContext>();
        var uow = provider.GetRequiredService<IUnitOfWork>();

        await uow.BeginTransactionAsync(ct).ConfigureAwait(false);

        var product = ClientProduct.Create(UniqueName("Rollback"), 30m, "Reference");
        db.Products.Add(product);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await uow.RollbackTransactionAsync(ct).ConfigureAwait(false);

        await using var verificationScope = scopeFactory.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<ClientDbContext>();
        if (await verificationDb.Products.IgnoreQueryFilters().AnyAsync(p => p.Id == product.Id, ct).ConfigureAwait(false))
            throw new InvalidOperationException("Rolled back transaction was persisted.");
    }

    private async Task EndTransactionScenarioAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.BeginTransactionAsync(ct).ConfigureAwait(false);
        await uow.EndTransactionAsync(ct).ConfigureAwait(false);
    }

    private async Task CancellationScenarioAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClientDbContext>();

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        try
        {
            await db.Products.CountAsync(cancelled.Token).ConfigureAwait(false);
            throw new InvalidOperationException("Cancelled database operation unexpectedly completed.");
        }
        catch (OperationCanceledException)
        {
            // Expected.
        }

        ct.ThrowIfCancellationRequested();
    }

    private async Task TenantModelCacheScenarioAsync(CancellationToken ct)
    {
        tenantContext.SetTenant(TenantA);

        await using var scopeA = scopeFactory.CreateAsyncScope();
        var contextA = scopeA.ServiceProvider.GetRequiredService<ClientDbContext>();
        _ = contextA.Model;
        await contextA.TenantDocuments.AsNoTracking().Take(1).ToListAsync(ct).ConfigureAwait(false);

        await using var scopeB = scopeFactory.CreateAsyncScope();
        var contextB = scopeB.ServiceProvider.GetRequiredService<ClientDbContext>();
        var tenantB = scopeB.ServiceProvider.GetRequiredService<ConsoleTenantContext>();
        tenantB.SetTenant(TenantB);
        _ = contextB.Model;
        await contextB.TenantDocuments.AsNoTracking().Take(1).ToListAsync(ct).ConfigureAwait(false);

        tenantContext.SetTenant(TenantA);
    }

    private async Task MigrationSeedScenarioAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClientDbContext>();

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
    }

    private async Task RetryScenarioAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClientDbContext>();
        var strategy = db.Database.CreateExecutionStrategy();
        var executed = false;

        await strategy.ExecuteAsync(async () =>
        {
            _ = await db.Products.AsNoTracking().Take(1).ToListAsync(ct).ConfigureAwait(false);
            executed = true;
        }).ConfigureAwait(false);

        if (!executed)
            throw new InvalidOperationException("The execution strategy did not execute the operation.");
    }

    private async Task AuditScenarioAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClientDbContext>();

        var product = ClientProduct.Create(UniqueName("Audit"), 40m, "Reference");
        db.Products.Add(product);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        if (product.CreatedOn == default)
            throw new InvalidOperationException("Audit interceptor did not populate CreatedOn.");

        product.ChangePrice(41m);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        if (product.ModifiedOn == default)
            throw new InvalidOperationException("Audit interceptor did not populate ModifiedOn.");
    }

    private async Task SoftDeleteScenarioAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClientDbContext>();

        var product = ClientProduct.Create(UniqueName("SoftDelete"), 50m, "Reference");
        db.Products.Add(product);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        db.Products.Remove(product);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        if (!product.IsDeleted || product.DeletedOn is null)
            throw new InvalidOperationException("Soft delete interceptor did not convert the delete.");

        if (await db.Products.AnyAsync(p => p.Id == product.Id, ct).ConfigureAwait(false))
            throw new InvalidOperationException("Soft delete global filter did not hide the entity.");

        if (!await db.Products.IgnoreQueryFilters().AnyAsync(p => p.Id == product.Id && p.IsDeleted, ct).ConfigureAwait(false))
            throw new InvalidOperationException("Soft-deleted entity was not found through IgnoreQueryFilters.");
    }

    private async Task ImmutableScenarioAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClientDbContext>();

        var auditLog = new DemoAuditLog
        {
            Id = Guid.NewGuid(),
            Action = "ReferenceClient",
            PerformedBy = "reference-client",
            Detail = "Immutable scenario",
            PerformedAt = DateTimeOffset.UtcNow
        };

        db.AuditLogs.Add(auditLog);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        db.Entry(auditLog).State = EntityState.Modified;

        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException("Immutable interceptor allowed an update.");
        }
        catch (InvalidOperationException)
        {
            db.Entry(auditLog).State = EntityState.Unchanged;
        }

        db.AuditLogs.Remove(auditLog);
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException("Immutable interceptor allowed a delete.");
        }
        catch (InvalidOperationException)
        {
            db.Entry(auditLog).State = EntityState.Unchanged;
        }
    }

    private async Task DomainEventScenarioAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClientDbContext>();
        var before = domainEventDispatcher.DispatchCount;
        var orderNumber = UniqueName("Event");

        var order = ClientOrder.Create(orderNumber, 125.50m, "Confirmed");
        order.Place();
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        if (domainEventDispatcher.DispatchCount != before + 1 ||
            domainEventDispatcher.LastEvent is not OrderPlacedEvent eventData ||
            eventData.OrderNumber != orderNumber)
        {
            throw new InvalidOperationException("Domain event interceptor did not dispatch the expected OrderPlacedEvent.");
        }
    }

    private async Task SlowQueryScenarioAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClientDbContext>();
        _ = await db.Products.AsNoTracking().Take(1).ToListAsync(ct).ConfigureAwait(false);
    }

    private async Task TenantFilterScenarioAsync(CancellationToken ct)
    {
        string titleA = UniqueName("FilterA");
        string titleB = UniqueName("FilterB");

        await AddTenantDocumentAsync(TenantA, titleA, ct).ConfigureAwait(false);
        await AddTenantDocumentAsync(TenantB, titleB, ct).ConfigureAwait(false);

        await using var scopeA = scopeFactory.CreateAsyncScope();
        var contextA = scopeA.ServiceProvider.GetRequiredService<ClientDbContext>();
        var tenantA = scopeA.ServiceProvider.GetRequiredService<ConsoleTenantContext>();
        tenantA.SetTenant(TenantA);

        if (!await contextA.TenantDocuments.AnyAsync(d => d.Title == titleA, ct).ConfigureAwait(false) ||
            await contextA.TenantDocuments.AnyAsync(d => d.Title == titleB, ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Tenant filter did not isolate Tenant A.");
        }

        await using var scopeB = scopeFactory.CreateAsyncScope();
        var contextB = scopeB.ServiceProvider.GetRequiredService<ClientDbContext>();
        var tenantB = scopeB.ServiceProvider.GetRequiredService<ConsoleTenantContext>();
        tenantB.SetTenant(TenantB);

        if (!await contextB.TenantDocuments.AnyAsync(d => d.Title == titleB, ct).ConfigureAwait(false) ||
            await contextB.TenantDocuments.AnyAsync(d => d.Title == titleA, ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Tenant filter did not isolate Tenant B.");
        }

        tenantContext.SetTenant(TenantA);
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

    private static string UniqueName(string prefix)
        => $"__KUKULCAN_REFERENCE_{prefix}_{Guid.NewGuid():N}__";
}
