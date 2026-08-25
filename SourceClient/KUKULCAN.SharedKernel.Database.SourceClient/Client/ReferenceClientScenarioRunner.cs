using KUKULCAN.SharedKernel.Database.Client.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KUKULCAN.SharedKernel.Database.Client;

/// <summary>Executes the provider-neutral reference scenarios exposed by the database client.</summary>
public sealed class ReferenceClientScenarioRunner(
    IServiceScopeFactory scopeFactory,
    ConsoleTenantContext tenantContext,
    ConsoleDomainEventDispatcher domainEventDispatcher)
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private ClientDbContext Db => scopeFactory.CreateScope().ServiceProvider.GetRequiredService<ClientDbContext>();

    /// <summary>Runs all reference scenarios.</summary>
    public async Task RunAllAsync(CancellationToken ct = default)
    {
        await ConfigurationScenarioAsync(ct).ConfigureAwait(false);
        await SaveChangesScenarioAsync(ct).ConfigureAwait(false);
        await TransactionCommitScenarioAsync(ct).ConfigureAwait(false);
        await TransactionRollbackScenarioAsync(ct).ConfigureAwait(false);
        await EndTransactionScenarioAsync(ct).ConfigureAwait(false);
        await CancellationScenarioAsync(ct).ConfigureAwait(false);
        await TenantModelCacheScenarioAsync(ct).ConfigureAwait(false);
        await MigrationSeedScenarioAsync(ct).ConfigureAwait(false);
        await RetryScenarioAsync(ct).ConfigureAwait(false);
        await AuditScenarioAsync(ct).ConfigureAwait(false);
        await SoftDeleteScenarioAsync(ct).ConfigureAwait(false);
        await ImmutableScenarioAsync(ct).ConfigureAwait(false);
        await DomainEventScenarioAsync(ct).ConfigureAwait(false);
        await SlowQueryScenarioAsync(ct).ConfigureAwait(false);
        await TenantFilterScenarioAsync(ct).ConfigureAwait(false);
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
        await using var db = Db;
        var product = ClientProduct.Create(UniqueName("Save"), 10m, "Reference");
        db.Products.Add(product);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (!await db.Products.AnyAsync(p => p.Id == product.Id, ct).ConfigureAwait(false))
            throw new InvalidOperationException("SaveChangesAsync did not persist the product.");
    }

    private async Task TransactionCommitScenarioAsync(CancellationToken ct)
    {
        await using var db = Db;
        var uow = scopeFactory.CreateScope().ServiceProvider.GetRequiredService<KUKULCAN.SharedKernel.Database.Abstractions.IUnitOfWork>();
        await uow.BeginTransactionAsync(ct).ConfigureAwait(false);
        var product = ClientProduct.Create(UniqueName("Commit"), 20m, "Reference");
        db.Products.Add(product);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await uow.CommitTransactionAsync(ct).ConfigureAwait(false);
        await uow.EndTransactionAsync().ConfigureAwait(false);
    }

    private async Task TransactionRollbackScenarioAsync(CancellationToken ct)
    {
        await using var db = Db;
        var uow = scopeFactory.CreateScope().ServiceProvider.GetRequiredService<KUKULCAN.SharedKernel.Database.Abstractions.IUnitOfWork>();
        await uow.BeginTransactionAsync(ct).ConfigureAwait(false);
        db.Products.Add(ClientProduct.Create(UniqueName("Rollback"), 30m, "Reference"));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await uow.RollbackTransactionAsync(ct).ConfigureAwait(false);
        await uow.EndTransactionAsync().ConfigureAwait(false);
    }

    private async Task EndTransactionScenarioAsync(CancellationToken ct)
    {
        await using var db = Db;
        var uow = scopeFactory.CreateScope().ServiceProvider.GetRequiredService<KUKULCAN.SharedKernel.Database.Abstractions.IUnitOfWork>();
        await uow.BeginTransactionAsync(ct).ConfigureAwait(false);
        await uow.EndTransactionAsync().ConfigureAwait(false);
    }

    private async Task CancellationScenarioAsync(CancellationToken ct)
    {
        await using var db = Db;
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
    }

    private async Task TenantModelCacheScenarioAsync(CancellationToken ct)
    {
        tenantContext.SetTenant(TenantA);
        await using var scopeA = scopeFactory.CreateAsyncScope();
        var contextA = scopeA.ServiceProvider.GetRequiredService<ClientDbContext>();
        if (!await contextA.TenantDocuments.AnyAsync(ct).ConfigureAwait(false))
            throw new InvalidOperationException("Tenant A model did not initialize correctly.");

        await using var scopeB = scopeFactory.CreateAsyncScope();
        var contextB = scopeB.ServiceProvider.GetRequiredService<ClientDbContext>();
        var tenantB = scopeB.ServiceProvider.GetRequiredService<ConsoleTenantContext>();
        tenantB.SetTenant(TenantB);
        _ = contextB.Model;
    }

    private async Task MigrationSeedScenarioAsync(CancellationToken ct)
    {
        await using var db = Db;
        var migrations = db.Database.GetMigrations().ToArray();
        if (migrations.Length > 0)
            await db.Database.MigrateAsync(ct).ConfigureAwait(false);
        await db.Database.EnsureCreatedAsync(ct).ConfigureAwait(false);
    }

    private async Task RetryScenarioAsync(CancellationToken ct)
    {
        await using var db = Db;
        await db.Database.CreateExecutionStrategy().ExecuteAsync(
            async () => await db.Products.AsNoTracking().CountAsync(ct).ConfigureAwait(false));
    }

    private async Task AuditScenarioAsync(CancellationToken ct)
    {
        await using var db = Db;
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
        await using var db = Db;
        var product = ClientProduct.Create(UniqueName("SoftDelete"), 50m, "Reference");
        db.Products.Add(product);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        db.Products.Remove(product);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (!product.IsDeleted || product.DeletedOn is null)
            throw new InvalidOperationException("Soft delete interceptor did not convert the delete.");
        if (await db.Products.AnyAsync(p => p.Id == product.Id, ct).ConfigureAwait(false))
            throw new InvalidOperationException("Soft delete global filter did not hide the entity.");
    }

    private async Task ImmutableScenarioAsync(CancellationToken ct)
    {
        await using var db = Db;
        var order = ClientOrder.Create(UniqueName("Immutable"));
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        db.Entry(order).State = EntityState.Modified;
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException("Immutable interceptor allowed an update.");
        }
        catch (InvalidOperationException)
        {
            db.Entry(order).State = EntityState.Unchanged;
        }
    }

    private async Task DomainEventScenarioAsync(CancellationToken ct)
    {
        await using var db = Db;
        int before = domainEventDispatcher.DispatchCount;
        var order = ClientOrder.Create(UniqueName("Event"));
        order.Place();
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (domainEventDispatcher.DispatchCount <= before)
            throw new InvalidOperationException("Domain event interceptor did not dispatch an event.");
    }

    private async Task SlowQueryScenarioAsync(CancellationToken ct)
    {
        await using var db = Db;
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
