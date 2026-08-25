using KUKULCAN.SharedKernel.Database.Abstractions;
using KUKULCAN.SharedKernel.Database.Client.Client;
using KUKULCAN.SharedKernel.Database.Configuration;
using KUKULCAN.SharedKernel.Database.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Spectre.Console;

namespace KUKULCAN.SharedKernel.Database.Client;

/// <summary>
/// Executes the provider-neutral reference scenarios exposed by the database client.
/// </summary>
public sealed class ReferenceClientScenarioRunner(
    IServiceScopeFactory scopeFactory,
    ConsoleTenantContext tenantContext,
    ConsoleDomainEventDispatcher domainEventDispatcher,
    IOptions<KukulcanDatabaseOptions> options)
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    /// <summary>
    /// Runs the complete provider-neutral reference scenario suite.
    /// </summary>
    public async Task RunAllAsync(CancellationToken ct = default)
    {
        var scenarios = new (string Name, Func<CancellationToken, Task> Run)[]
        {
            ("Configuration / provider", ConfigurationScenarioAsync),
            ("SaveChangesAsync", SaveChangesScenarioAsync),
            ("Transaction / Commit", TransactionCommitScenarioAsync),
            ("Transaction / Rollback", TransactionRollbackScenarioAsync),
            ("Transaction / EndTransaction", EndTransactionScenarioAsync),
            ("Cancellation", CancellationScenarioAsync),
            ("Tenant Model Cache", TenantModelCacheScenarioAsync),
            ("Migrations / Seed", MigrationSeedScenarioAsync),
            ("Retry / Execution Strategy", RetryScenarioAsync),
            ("Audit interceptor", AuditScenarioAsync),
            ("Soft Delete / global filter", SoftDeleteScenarioAsync),
            ("Immutable interceptor", ImmutableScenarioAsync),
            ("Domain Events", DomainEventScenarioAsync),
            ("Slow Query", SlowQueryScenarioAsync),
            ("Tenant Filter", TenantFilterScenarioAsync)
        };

        foreach (var scenario in scenarios)
        {
            ct.ThrowIfCancellationRequested();
            await scenario.Run(ct).ConfigureAwait(false);
            AnsiConsole.MarkupLine($"[green]✔ PASS[/] {Markup.Escape(scenario.Name)}");
        }
    }

    private Task ConfigurationScenarioAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClientDbContext>();
        var configuredProvider = options.Value.Provider;
        var providerName = db.Database.ProviderName;

        if (string.IsNullOrWhiteSpace(options.Value.ConnectionString))
            throw new InvalidOperationException("The configured database connection string is empty.");

        if (configuredProvider is not (DatabaseProvider.SqlServer or DatabaseProvider.PostgresSql or DatabaseProvider.MySql))
            throw new InvalidOperationException($"Unsupported provider: {configuredProvider}.");

        if (string.IsNullOrWhiteSpace(providerName))
            throw new InvalidOperationException("EF Core did not expose a configured database provider.");

        var expectedProviderText = configuredProvider switch
        {
            DatabaseProvider.SqlServer => "SqlServer",
            DatabaseProvider.PostgresSql => "Npgsql",
            DatabaseProvider.MySql => "MySQL",
            _ => throw new InvalidOperationException()
        };

        if (!providerName.Contains(expectedProviderText, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Configured provider '{configuredProvider}' resolved to unexpected EF Core provider '{providerName}'.");

        return Task.CompletedTask;
    }

    private async Task SaveChangesScenarioAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClientDbContext>();

        var product = ClientProduct.Create(UniqueName("Save"), 10m, "Reference");
        db.Products.Add(product);
        var affected = await db.SaveChangesAsync(ct).ConfigureAwait(false);

        if (affected <= 0)
            throw new InvalidOperationException("SaveChangesAsync reported no persisted entries.");

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
        var provider = scope.ServiceProvider;
        var db = provider.GetRequiredService<ClientDbContext>();
        var uow = provider.GetRequiredService<IUnitOfWork>();

        await uow.BeginTransactionAsync(ct).ConfigureAwait(false);

        var product = ClientProduct.Create(UniqueName("EndTransaction"), 40m, "Reference");
        db.Products.Add(product);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await uow.EndTransactionAsync(ct).ConfigureAwait(false);

        await using var verificationScope = scopeFactory.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<ClientDbContext>();
        if (await verificationDb.Products.IgnoreQueryFilters().AnyAsync(p => p.Id == product.Id, ct).ConfigureAwait(false))
            throw new InvalidOperationException("EndTransactionAsync left transaction data committed.");
    }

    private async Task CancellationScenarioAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClientDbContext>();

        var product = ClientProduct.Create(UniqueName("Cancellation"), 45m, "Reference");
        db.Products.Add(product);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        try
        {
            await db.SaveChangesAsync(cancelled.Token).ConfigureAwait(false);
            throw new InvalidOperationException("Cancelled SaveChangesAsync unexpectedly completed.");
        }
        catch (OperationCanceledException)
        {
            // Expected.
        }

        db.ChangeTracker.Clear();
        ct.ThrowIfCancellationRequested();
    }

    private async Task TenantModelCacheScenarioAsync(CancellationToken ct)
    {
        var titleA = UniqueName("TenantModelA");
        var titleB = UniqueName("TenantModelB");

        await using (var scopeA = scopeFactory.CreateAsyncScope())
        {
            var tenantA = scopeA.ServiceProvider.GetRequiredService<ConsoleTenantContext>();
            tenantA.SetTenant(TenantA);
            var contextA = scopeA.ServiceProvider.GetRequiredService<ClientDbContext>();
            contextA.TenantDocuments.Add(DemoTenantDocument.Create(TenantA, titleA, "Tenant A"));
            await contextA.SaveChangesAsync(ct).ConfigureAwait(false);
            _ = contextA.Model;
        }

        await using (var scopeB = scopeFactory.CreateAsyncScope())
        {
            var tenantB = scopeB.ServiceProvider.GetRequiredService<ConsoleTenantContext>();
            tenantB.SetTenant(TenantB);
            var contextB = scopeB.ServiceProvider.GetRequiredService<ClientDbContext>();
            contextB.TenantDocuments.Add(DemoTenantDocument.Create(TenantB, titleB, "Tenant B"));
            await contextB.SaveChangesAsync(ct).ConfigureAwait(false);
            _ = contextB.Model;

            if (!await contextB.TenantDocuments.AnyAsync(d => d.Title == titleB, ct).ConfigureAwait(false) ||
                await contextB.TenantDocuments.AnyAsync(d => d.Title == titleA, ct).ConfigureAwait(false))
            {
                throw new InvalidOperationException("Tenant model cache/filter exposed data from another tenant.");
            }
        }

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

        if (!await db.Products.IgnoreQueryFilters().AnyAsync(p => p.Name == seedName, ct).ConfigureAwait(false))
            throw new InvalidOperationException("Reference seed data was not persisted.");
    }

    private async Task RetryScenarioAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClientDbContext>();
        var strategy = db.Database.CreateExecutionStrategy();

        if (options.Value.Retry.Enabled && !strategy.RetriesOnFailure)
            throw new InvalidOperationException("Retry is enabled in configuration but the selected provider execution strategy does not retry.");

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

        var createdOn = product.CreatedOn;
        if (createdOn == default)
            throw new InvalidOperationException("Audit interceptor did not populate CreatedOn.");

        product.ChangePrice(41m);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        if (product.ModifiedOn is null || product.ModifiedOn <= createdOn)
            throw new InvalidOperationException("Audit interceptor did not populate ModifiedOn on update.");
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
        await ExpectInvalidOperationAsync(
            () => db.SaveChangesAsync(ct),
            "Immutable interceptor allowed an update.").ConfigureAwait(false);
        db.Entry(auditLog).State = EntityState.Unchanged;

        db.AuditLogs.Remove(auditLog);
        await ExpectInvalidOperationAsync(
            () => db.SaveChangesAsync(ct),
            "Immutable interceptor allowed a delete.").ConfigureAwait(false);
        db.Entry(auditLog).State = EntityState.Unchanged;
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
        var previousThreshold = SlowQueryInterceptor.SlowQueryThresholdMs;
        SlowQueryInterceptor.SlowQueryThresholdMs = 0;

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ClientDbContext>();
            _ = await db.Products.AsNoTracking().Take(1).ToListAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            SlowQueryInterceptor.SlowQueryThresholdMs = previousThreshold;
        }
    }

    private async Task TenantFilterScenarioAsync(CancellationToken ct)
    {
        string titleA = UniqueName("FilterA");
        string titleB = UniqueName("FilterB");

        await AddTenantDocumentAsync(TenantA, titleA, "Tenant A", ct).ConfigureAwait(false);
        await AddTenantDocumentAsync(TenantB, titleB, "Tenant B", ct).ConfigureAwait(false);

        await using (var scopeA = scopeFactory.CreateAsyncScope())
        {
            var tenantA = scopeA.ServiceProvider.GetRequiredService<ConsoleTenantContext>();
            tenantA.SetTenant(TenantA);
            var contextA = scopeA.ServiceProvider.GetRequiredService<ClientDbContext>();

            if (!await contextA.TenantDocuments.AnyAsync(d => d.Title == titleA, ct).ConfigureAwait(false) ||
                await contextA.TenantDocuments.AnyAsync(d => d.Title == titleB, ct).ConfigureAwait(false))
            {
                throw new InvalidOperationException("Tenant filter did not isolate Tenant A.");
            }
        }

        await using (var scopeB = scopeFactory.CreateAsyncScope())
        {
            var tenantB = scopeB.ServiceProvider.GetRequiredService<ConsoleTenantContext>();
            tenantB.SetTenant(TenantB);
            var contextB = scopeB.ServiceProvider.GetRequiredService<ClientDbContext>();

            if (!await contextB.TenantDocuments.AnyAsync(d => d.Title == titleB, ct).ConfigureAwait(false) ||
                await contextB.TenantDocuments.AnyAsync(d => d.Title == titleA, ct).ConfigureAwait(false))
            {
                throw new InvalidOperationException("Tenant filter did not isolate Tenant B.");
            }
        }

        tenantContext.SetTenant(TenantA);
    }

    private async Task AddTenantDocumentAsync(Guid tenantId, string title, string content, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ConsoleTenantContext>();
        tenant.SetTenant(tenantId);
        var context = scope.ServiceProvider.GetRequiredService<ClientDbContext>();
        context.TenantDocuments.Add(DemoTenantDocument.Create(tenantId, title, content));
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static async Task ExpectInvalidOperationAsync(Func<Task> action, string message)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    private static string UniqueName(string prefix)
        => $"__KUKULCAN_REFERENCE_{prefix}_{Guid.NewGuid():N}__";
}
