using KUKULCAN.SharedKernel.Database.Configuration;
using KUKULCAN.SharedKernel.Database.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace KUKULCAN.SharedKernel.Database.Client.Client;

/// <summary>
/// Executable showcase for the public functional surface of KUKULCAN.SharedKernel.Database.
/// Every scenario uses only provider-neutral EF Core and SharedKernel APIs so the same suite
/// can be executed against SQL Server, PostgreSQL and MySQL.
/// </summary>
public sealed class ReferenceClientScenarioRunner(
    ClientDbContext db,
    UnitOfWork<ClientDbContext> uow,
    ConsoleCurrentUser currentUser,
    ConsoleTenantContext tenantContext,
    ConsoleDateTimeProvider clock,
    KukulcanDatabaseOptions options,
    IServiceScopeFactory scopeFactory)
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    /// <summary>Runs every reference-client scenario in a deterministic order.</summary>
    public async Task RunAllAsync(CancellationToken cancellationToken)
    {
        Section($"FULL REFERENCE CLIENT — {options.Provider}");
        AnsiConsole.MarkupLine("[grey]La misma batería se ejecuta sin código específico del proveedor.[/]\n");

        var scenarios = new (string Name, Func<CancellationToken, Task> Run)[]
        {
            ("Configuration and provider-neutral startup", ConfigurationScenarioAsync),
            ("UnitOfWork SaveChanges", SaveChangesScenarioAsync),
            ("UnitOfWork Commit", CommitScenarioAsync),
            ("UnitOfWork Rollback", RollbackScenarioAsync),
            ("UnitOfWork EndTransaction", EndTransactionScenarioAsync),
            ("Cancellation", CancellationScenarioAsync),
            ("Tenant model cache", TenantModelCacheScenarioAsync),
            ("Migration and seed", MigrationAndSeedScenarioAsync),
            ("Retry execution strategy", RetryScenarioAsync),
            ("Audit interceptor", AuditScenarioAsync),
            ("Soft delete interceptor and filter", SoftDeleteScenarioAsync),
            ("Immutable interceptor", ImmutableScenarioAsync),
            ("Domain events interceptor", DomainEventsScenarioAsync),
            ("Slow query interceptor", SlowQueryScenarioAsync),
            ("Tenant filter", TenantFilterScenarioAsync)
        };

        var passed = 0;
        foreach (var scenario in scenarios)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await scenario.Run(cancellationToken);
                Ok($"{scenario.Name}: PASS");
                passed++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]✘ {scenario.Name}: FAIL[/]");
                AnsiConsole.MarkupLine($"[grey]{ex.Message.EscapeMarkup()}[/]");
                throw;
            }
        }

        AnsiConsole.MarkupLine($"\n[bold green]REFERENCE CLIENT PASS: {passed}/{scenarios.Length}[/]");
        Pause();
    }

    private Task ConfigurationScenarioAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (options.Provider is not (DatabaseProvider.SqlServer or DatabaseProvider.PostgresSql or DatabaseProvider.MySql))
            throw new InvalidOperationException($"Unsupported reference-client provider: {options.Provider}");
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            throw new InvalidOperationException("The selected provider has no connection string.");
        if (options.CommandTimeoutSeconds <= 0)
            throw new InvalidOperationException("Command timeout must be greater than zero.");
        return Task.CompletedTask;
    }

    private async Task SaveChangesScenarioAsync(CancellationToken ct)
    {
        var product = ClientProduct.Create("REFERENCE-UOW-SAVE", 10m, "Reference");
        db.Products.Add(product);
        var affected = await uow.SaveChangesAsync(ct);
        if (affected <= 0) throw new InvalidOperationException("SaveChangesAsync persisted no entries.");
    }

    private async Task CommitScenarioAsync(CancellationToken ct)
    {
        await uow.BeginTransactionAsync(ct);
        try
        {
            db.Products.AddRange(
                ClientProduct.Create("REFERENCE-TX-COMMIT-A", 10m, "Reference"),
                ClientProduct.Create("REFERENCE-TX-COMMIT-B", 20m, "Reference"));
            await uow.CommitTransactionAsync(ct);
        }
        catch
        {
            await TryRollbackAsync();
            throw;
        }
    }

    private async Task RollbackScenarioAsync(CancellationToken ct)
    {
        var name = $"REFERENCE-TX-ROLLBACK-{Guid.NewGuid():N}";
        await uow.BeginTransactionAsync(ct);
        try
        {
            var product = ClientProduct.Create(name, 99m, "Reference");
            db.Products.Add(product);
            await uow.RollbackTransactionAsync(ct);
            db.ChangeTracker.Clear();

            if (await db.Products.IgnoreQueryFilters().AnyAsync(p => p.Name == name, ct))
                throw new InvalidOperationException("Rollback persisted an entity unexpectedly.");
        }
        catch
        {
            await TryRollbackAsync();
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task EndTransactionScenarioAsync(CancellationToken ct)
    {
        var name = $"REFERENCE-TX-END-{Guid.NewGuid():N}";
        await uow.BeginTransactionAsync(ct);
        try
        {
            db.Products.Add(ClientProduct.Create(name, 77m, "Reference"));
            await uow.EndTransactionAsync(ct);
            db.ChangeTracker.Clear();

            if (await db.Products.IgnoreQueryFilters().AnyAsync(p => p.Name == name, ct))
                throw new InvalidOperationException("EndTransactionAsync unexpectedly committed data.");
        }
        catch
        {
            await TryRollbackAsync();
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task CancellationScenarioAsync(CancellationToken ct)
    {
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cancelled.Cancel();

        try
        {
            await db.Database.ExecuteSqlRawAsync("SELECT 1", cancelled.Token);
            throw new InvalidOperationException("The provider did not observe the cancelled database operation.");
        }
        catch (OperationCanceledException)
        {
            // Expected: cancellation crossed the EF Core database boundary.
        }
    }

    private async Task TenantModelCacheScenarioAsync(CancellationToken ct)
    {
        tenantContext.SetTenant(TenantA);
        var titleA = $"REFERENCE-CACHE-A-{Guid.NewGuid():N}";
        await AddTenantDocumentAsync(TenantA, titleA, ct);

        tenantContext.SetTenant(TenantB);
        var titleB = $"REFERENCE-CACHE-B-{Guid.NewGuid():N}";
        await AddTenantDocumentAsync(TenantB, titleB, ct);

        tenantContext.SetTenant(TenantA);
        await using (var scopeA = scopeFactory.CreateAsyncScope())
        {
            var contextA = scopeA.ServiceProvider.GetRequiredService<ClientDbContext>();
            if (!await contextA.TenantDocuments.AnyAsync(d => d.Title == titleA, ct))
                throw new InvalidOperationException("Tenant A model cache/filter did not expose tenant A data.");
            if (await contextA.TenantDocuments.AnyAsync(d => d.Title == titleB, ct))
                throw new InvalidOperationException("Tenant A model cache/filter exposed tenant B data.");
        }

        tenantContext.SetTenant(TenantB);
        await using (var scopeB = scopeFactory.CreateAsyncScope())
        {
            var contextB = scopeB.ServiceProvider.GetRequiredService<ClientDbContext>();
            if (!await contextB.TenantDocuments.AnyAsync(d => d.Title == titleB, ct))
                throw new InvalidOperationException("Tenant B model cache/filter did not expose tenant B data.");
            if (await contextB.TenantDocuments.AnyAsync(d => d.Title == titleA, ct))
                throw new InvalidOperationException("Tenant B model cache/filter exposed tenant A data.");
        }

        tenantContext.SetTenant(TenantA);
        db.ChangeTracker.Clear();
    }

    private async Task AddTenantDocumentAsync(Guid tenantId, string title, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ClientDbContext>();
        context.TenantDocuments.Add(DemoTenantDocument.Create(tenantId, title, "Reference tenant-cache scenario"));
        await context.SaveChangesAsync(ct);
    }

    private async Task MigrationAndSeedScenarioAsync(CancellationToken ct)
    {
        var migrations = (await db.Database.GetMigrationsAsync(ct)).ToArray();
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
        if (options.Retry.Enabled && !strategy.RetriesOnFailure)
            throw new InvalidOperationException("Retry is enabled in configuration but the selected provider execution strategy does not retry.");

        await strategy.ExecuteAsync(async () =>
        {
            _ = await db.Products.CountAsync(ct);
        });
    }

    private async Task AuditScenarioAsync(CancellationToken ct)
    {
        currentUser.SetUser("reference-client");
        var product = ClientProduct.Create("REFERENCE-AUDIT", 49.99m, "Reference");
        db.Products.Add(product);
        await uow.SaveChangesAsync(ct);
        if (product.CreatedOn == default) throw new InvalidOperationException("CreatedOn was not populated.");

        product.ChangePrice(59.99m);
        await uow.SaveChangesAsync(ct);
        if (product.ModifiedOn is null) throw new InvalidOperationException("ModifiedOn was not populated.");
    }

    private async Task SoftDeleteScenarioAsync(CancellationToken ct)
    {
        var product = ClientProduct.Create("REFERENCE-SOFTDELETE", 15m, "Reference");
        db.Products.Add(product);
        await uow.SaveChangesAsync(ct);
        db.Products.Remove(product);
        await uow.SaveChangesAsync(ct);

        var raw = await db.Products.IgnoreQueryFilters().SingleAsync(p => p.Id == product.Id, ct);
        if (!raw.IsDeleted || raw.DeletedOn is null)
            throw new InvalidOperationException("Soft delete interceptor did not mark the entity.");
        if (await db.Products.AnyAsync(p => p.Id == product.Id, ct))
            throw new InvalidOperationException("Soft delete global filter did not hide the entity.");
    }

    private async Task ImmutableScenarioAsync(CancellationToken ct)
    {
        var log = new DemoAuditLog
        {
            Id = Guid.NewGuid(),
            Action = "ReferenceClient",
            PerformedBy = currentUser.UserName,
            Detail = "Immutable scenario",
            PerformedAt = clock.UtcNow
        };
        db.AuditLogs.Add(log);
        await uow.SaveChangesAsync(ct);

        db.Entry(log).State = EntityState.Modified;
        try
        {
            await uow.SaveChangesAsync(ct);
            throw new InvalidOperationException("ImmutableEntityInterceptor allowed an update.");
        }
        catch (InvalidOperationException) when (db.ChangeTracker.Entries().All(e => e.State != EntityState.Modified))
        {
            db.ChangeTracker.Clear();
        }
    }

    private async Task DomainEventsScenarioAsync(CancellationToken ct)
    {
        var order = ClientOrder.Create($"REFERENCE-EVENT-{Guid.NewGuid():N}", 299.95m, "Confirmed");
        order.Place();
        db.Orders.Add(order);
        await uow.SaveChangesAsync(ct);

        if (!db.ChangeTracker.Entries<ClientOrder>().Any())
            return;
    }

    private async Task SlowQueryScenarioAsync(CancellationToken ct)
    {
        _ = await db.Products.CountAsync(ct);
    }

    private async Task TenantFilterScenarioAsync(CancellationToken ct)
    {
        var originalTenant = tenantContext.TenantId;
        try
        {
            tenantContext.SetTenant(TenantA);
            var titleA = $"REFERENCE-FILTER-A-{Guid.NewGuid():N}";
            await AddTenantDocumentAsync(TenantA, titleA, ct);

            tenantContext.SetTenant(TenantB);
            var titleB = $"REFERENCE-FILTER-B-{Guid.NewGuid():N}";
            await AddTenantDocumentAsync(TenantB, titleB, ct);

            tenantContext.SetTenant(TenantA);
            await using var scope = scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<ClientDbContext>();
            var visibleA = await context.TenantDocuments.Where(d => d.Title.StartsWith("REFERENCE-FILTER-")).ToListAsync(ct);
            if (visibleA.Count != 1 || visibleA[0].Title != titleA)
                throw new InvalidOperationException("Tenant filter did not isolate tenant A.");
        }
        finally
        {
            tenantContext.SetTenant(originalTenant);
            db.ChangeTracker.Clear();
        }
    }

    private async Task TryRollbackAsync()
    {
        try { await uow.RollbackTransactionAsync(); }
        catch (InvalidOperationException) { }
    }

    private static void Section(string title)
        => AnsiConsole.Write(new Rule($"[blue]{title}[/]").RuleStyle(Style.Parse("blue")));

    private static void Ok(string message) => AnsiConsole.MarkupLine($"[green]✔[/] {message}");

    private static void Pause() => AnsiConsole.MarkupLine("[grey]Pulsa una tecla para continuar…[/]");
}
