using KUKULCAN.SharedKernel.Database.Client.Client;
using KUKULCAN.SharedKernel.Database.Configuration;
using KUKULCAN.SharedKernel.Database.Interceptors;
using KUKULCAN.SharedKernel.Database.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Spectre.Console;

namespace KUKULCAN.SharedKernel.Database.Client.UI;

/// <summary>
/// Interactive menu that demonstrates each feature of KUKULCAN.Kernel.Database.
/// </summary>
public sealed class ConsoleMenu(ClientDbContext db, UnitOfWork<ClientDbContext> uow, ConsoleCurrentUser currentUser,
    ConsoleTenantContext tenantContext, ConsoleDateTimeProvider clock, KukulcanDatabaseOptions opts)
{
    // ══════════════════════════════════════════════════════════════════════════
    // Entry point
    // ══════════════════════════════════════════════════════════════════════════

    public async Task RunAsync(CancellationToken ct)
    {
        AnsiConsole.Clear();
        PrintBanner();

        // Crear esquema / migrar en demo
        await AnsiConsole.Status().StartAsync("Aplicando migraciones…", async _ =>
        {
            await db.Database.EnsureCreatedAsync(ct);
        });
        AnsiConsole.MarkupLine("[green]✔[/] Base de datos lista.\n");

        while (!ct.IsCancellationRequested)
        {
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[bold yellow]── Menú principal ──[/]")
                    .AddChoices(
                        "1. Configuración actual",
                        "2. Unit of Work",
                        "3. Interceptor — Audit (AuditSaveChangesInterceptor)",
                        "4. Interceptor — Soft Delete (SoftDeleteInterceptor)",
                        "5. Interceptor — Entidad Inmutable (ImmutableEntityInterceptor)",
                        "6. Interceptor — Domain Events (DomainEventDispatchInterceptor)",
                        "7. Interceptor — Slow Query (SlowQueryInterceptor)",
                        "8. Filtro global — Soft Delete Filter",
                        "9. Filtro global — Tenant Filter",
                        "0. Salir"));

            if (choice.StartsWith("0")) break;

            await (choice[0] switch
            {
                '1' => ShowConfigAsync(),
                '2' => UnitOfWorkMenuAsync(ct),
                '3' => AuditInterceptorDemoAsync(ct),
                '4' => SoftDeleteInterceptorDemoAsync(ct),
                '5' => ImmutableInterceptorDemoAsync(ct),
                '6' => DomainEventInterceptorDemoAsync(ct),
                '7' => SlowQueryInterceptorDemoAsync(ct),
                '8' => SoftDeleteFilterDemoAsync(ct),
                '9' => TenantFilterDemoAsync(ct),
                _   => Task.CompletedTask
            });
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 1 · Configuración actual
    // ══════════════════════════════════════════════════════════════════════════

    private Task ShowConfigAsync()
    {
        AnsiConsole.Write(new Rule("[blue]Configuración — KukulkanDatabaseOptions[/]").RuleStyle(Style.Parse("blue")));

        var t = new Table().AddColumn("Clave").AddColumn("Valor");
        t.AddRow("Provider",                  $"[cyan]{opts.Provider}[/]");
        t.AddRow("CommandTimeoutSeconds",      opts.CommandTimeoutSeconds.ToString());
        t.AddRow("EnableSensitiveDataLogging", opts.EnableSensitiveDataLogging ? "[yellow]true[/]" : "false");
        t.AddRow("EnableDetailedErrors",       opts.EnableDetailedErrors        ? "[yellow]true[/]" : "false");
        t.AddRow("Retry:Enabled",             opts.Retry.Enabled.ToString());
        t.AddRow("Retry:MaxRetryCount",       opts.Retry.MaxRetryCount.ToString());
        t.AddRow("Retry:MaxRetryDelaySeconds",opts.Retry.MaxRetryDelaySeconds.ToString());
        t.AddRow("Pool:MinSize",              opts.Pool.MinSize.ToString());
        t.AddRow("Pool:MaxSize",              opts.Pool.MaxSize.ToString());
        t.AddRow("Migration:AutoMigrate",     opts.Migration.AutoMigrateOnStartup.ToString());
        t.AddRow("Migration:SeedData",        opts.Migration.SeedDataOnStartup.ToString());

        AnsiConsole.Write(t);

        AnsiConsole.MarkupLine($"\n[grey]Usuario actual:[/] [green]{currentUser.UserName}[/]" +
                               (currentUser.IsAuthenticated ? " [green](autenticado)[/]" : " [red](anónimo)[/]"));
        AnsiConsole.MarkupLine($"[grey]Tenant actual:[/]  [cyan]{tenantContext.TenantId}[/]");
        AnsiConsole.MarkupLine($"[grey]Hora actual:  [/]  {clock.UtcNow:u}");

        Pause();
        return Task.CompletedTask;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 2 · Unit of Work
    // ══════════════════════════════════════════════════════════════════════════

    private async Task UnitOfWorkMenuAsync(CancellationToken ct)
    {
        var action = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title("[blue]Unit of Work[/]")
            .AddChoices(
                "SaveChangesAsync — CRUD básico de producto",
                "Transacción — Commit (dos productos en una tx)",
                "Transacción — Rollback (simular error)",
                "← Volver"));

        switch (action)
        {
            case var s when s.StartsWith("SaveChanges"):
                await SaveChangesDemoAsync(ct);
                break;
            case var s when s.StartsWith("Transacción — Commit"):
                await TransactionCommitDemoAsync(ct);
                break;
            case var s when s.StartsWith("Transacción — Rollback"):
                await TransactionRollbackDemoAsync(ct);
                break;
        }
    }

    private async Task SaveChangesDemoAsync(CancellationToken ct)
    {
        Section("UnitOfWork — SaveChangesAsync");

        var product = ClientProduct.Create(
            name:     Ask("Nombre del producto"),
            price:    decimal.Parse(Ask("Precio")),
            category: Ask("Categoría"));

        db.Products.Add(product);
        var saved = await uow.SaveChangesAsync(ct);

        Ok($"SaveChangesAsync → {saved} fila(s) guardada(s). Id: [cyan]{product.Id}[/]");
        Pause();
    }

    private async Task TransactionCommitDemoAsync(CancellationToken ct)
    {
        Section("UnitOfWork — BeginTransactionAsync + CommitTransactionAsync");

        await uow.BeginTransactionAsync(ct);
        AnsiConsole.MarkupLine("[grey]Transacción iniciada.[/]");

        var p1 = ClientProduct.Create("TX-Producto-A", 10m, "Demo");
        var p2 = ClientProduct.Create("TX-Producto-B", 20m, "Demo");
        db.Products.AddRange(p1, p2);

        AnsiConsole.MarkupLine("[grey]Dos productos añadidos al contexto…[/]");

        await uow.CommitTransactionAsync(ct);
        Ok("CommitTransactionAsync — ambos productos confirmados en BD.");
        Pause();
    }

    private async Task TransactionRollbackDemoAsync(CancellationToken ct)
    {
        Section("UnitOfWork — BeginTransactionAsync + RollbackTransactionAsync");

        await uow.BeginTransactionAsync(ct);
        AnsiConsole.MarkupLine("[grey]Transacción iniciada.[/]");

        var p = ClientProduct.Create("TX-Rollback", 99m, "Demo");
        db.Products.Add(p);
        AnsiConsole.MarkupLine($"[grey]Producto '{p.Name}' añadido al contexto. Simulando error…[/]");

        await uow.RollbackTransactionAsync(ct);
        AnsiConsole.MarkupLine("[yellow]⚠ RollbackTransactionAsync — ningún cambio persistido.[/]");

        var exists = await db.Products.IgnoreQueryFilters().AnyAsync(x => x.Id == p.Id, ct);
        AnsiConsole.MarkupLine($"¿Existe en BD? [cyan]{exists}[/]  (esperado: [green]False[/])");
        Pause();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 3 · AuditSaveChangesInterceptor
    // ══════════════════════════════════════════════════════════════════════════

    private async Task AuditInterceptorDemoAsync(CancellationToken ct)
    {
        Section("AuditSaveChangesInterceptor — auto-poblado de campos de auditoría");

        AnsiConsole.MarkupLine("[grey]El interceptor rellena CreatedOn en INSERT y ModifiedOn en UPDATE automáticamente.[/]\n");

        var userName = Ask("Introduce un nombre de usuario para la demo");
        currentUser.SetUser(userName);

        // INSERT
        var product = ClientProduct.Create("Producto-Audit-Demo", 49.99m, "Audit");
        db.Products.Add(product);
        await uow.SaveChangesAsync(ct);

        PrintAuditRow("Tras INSERT", product.CreatedOn, product.ModifiedOn);

        // UPDATE
        product.ChangePrice(59.99m);
        db.Products.Update(product);
        await uow.SaveChangesAsync(ct);

        PrintAuditRow("Tras UPDATE", product.CreatedOn, product.ModifiedOn);

        AnsiConsole.MarkupLine("\n[green]Ningún código de dominio tocó estos campos — todo vía interceptor.[/]");
        Pause();
    }

    private static void PrintAuditRow(string label, DateTimeOffset createdOn, DateTimeOffset? modifiedOn)
    {
        var t = new Table().AddColumn("Estado").AddColumn("CreatedOn").AddColumn("ModifiedOn");
        t.AddRow(label,
            createdOn.ToString("u"),
            modifiedOn?.ToString("u") ?? "[grey]null[/]");
        AnsiConsole.Write(t);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 4 · SoftDeleteInterceptor
    // ══════════════════════════════════════════════════════════════════════════

    private async Task SoftDeleteInterceptorDemoAsync(CancellationToken ct)
    {
        Section("SoftDeleteInterceptor — DELETE físico → UPDATE con IsDeleted=true");

        // Crear producto de prueba
        var product = ClientProduct.Create("Producto-SoftDelete-Demo", 15m, "SoftDelete");
        db.Products.Add(product);
        await uow.SaveChangesAsync(ct);
        AnsiConsole.MarkupLine($"[grey]Producto creado:[/] Id=[cyan]{product.Id}[/]  IsDeleted=[green]false[/]");

        // Eliminar (el interceptor lo convierte en soft-delete)
        db.Products.Remove(product);
        await uow.SaveChangesAsync(ct);
        AnsiConsole.MarkupLine("[grey]context.Products.Remove(product) + SaveChangesAsync() ejecutado.[/]");

        // Verificar con IgnoreQueryFilters
        var raw = await db.Products
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == product.Id, ct);

        if (raw is not null)
        {
            var t = new Table().AddColumn("Campo").AddColumn("Valor");
            t.AddRow("IsDeleted", $"[yellow]{raw.IsDeleted}[/]");
            t.AddRow("DeletedAt", raw.DeletedOn?.ToString("u") ?? "[grey]null[/]");
            AnsiConsole.Write(t);
            AnsiConsole.MarkupLine("[green]✔ Registro NO eliminado físicamente — marcado como deleted.[/]");
        }

        // Verificar que el filtro global lo oculta
        var visibleCount = await db.Products.CountAsync(p => p.Id == product.Id, ct);
        AnsiConsole.MarkupLine($"\nConsulta normal (con filtro global): [cyan]{visibleCount}[/] resultado(s) — esperado [green]0[/].");
        Pause();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 5 · ImmutableEntityInterceptor
    // ══════════════════════════════════════════════════════════════════════════

    private async Task ImmutableInterceptorDemoAsync(CancellationToken ct)
    {
        Section("ImmutableEntityInterceptor — bloqueo de modificación/borrado");

        // Insertar una entrada de auditoría (permitido)
        var log = new DemoAuditLog
        {
            Id          = Guid.NewGuid(),
            Action      = "UserLogin",
            PerformedBy = currentUser.UserName,
            Detail      = "Demo login desde consola",
            PerformedAt = clock.UtcNow
        };
        db.AuditLogs.Add(log);
        await uow.SaveChangesAsync(ct);
        Ok($"AuditLog insertado correctamente. Id: [cyan]{log.Id}[/]");

        // Demostrar que la Actualización está bloqueada
        AnsiConsole.MarkupLine("\n[grey]Intentando modificar el campo Action (debería lanzar excepción)…[/]");
        try
        {
            var entry = db.AuditLogs.Local.First(a => a.Id == log.Id);
            // Forzar Modified en el tracker de EF
            db.Entry(entry).State = EntityState.Modified;
            await uow.SaveChangesAsync(ct);
            AnsiConsole.MarkupLine("[red]✘ Sin excepción — revisar interceptor.[/]");
        }
        catch (InvalidOperationException ex)
        {
            AnsiConsole.MarkupLine($"[green]✔ InvalidOperationException capturada:[/]");
            AnsiConsole.MarkupLine($"  [grey]{ex.Message.EscapeMarkup()}[/]");
            // Restaurar estado
            db.ChangeTracker.Clear();
        }

        // Demostrar que el Borrado está bloqueado
        AnsiConsole.MarkupLine("\n[grey]Intentando eliminar el AuditLog (debería lanzar excepción)…[/]");
        try
        {
            var fresh = await db.AuditLogs.FindAsync([log.Id], ct);
            if (fresh is not null) db.AuditLogs.Remove(fresh);
            await uow.SaveChangesAsync(ct);
            AnsiConsole.MarkupLine("[red]✘ Sin excepción — revisar interceptor.[/]");
        }
        catch (InvalidOperationException ex)
        {
            AnsiConsole.MarkupLine($"[green]✔ InvalidOperationException capturada:[/]");
            AnsiConsole.MarkupLine($"  [grey]{ex.Message.EscapeMarkup()}[/]");
            db.ChangeTracker.Clear();
        }

        Pause();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 6 · DomainEventDispatchInterceptor
    // ══════════════════════════════════════════════════════════════════════════

    private async Task DomainEventInterceptorDemoAsync(CancellationToken ct)
    {
        Section("DomainEventDispatchInterceptor — dispatch tras SaveChanges");

        AnsiConsole.MarkupLine("[grey]Los eventos se despachan DESPUÉS del commit para garantizar " +
                               "que la BD ya contiene el estado nuevo.[/]\n");

        var order = ClientOrder.Create(
            orderNumber: $"ORD-{DateTime.UtcNow:yyyyMMdd-HHmmss}",
            totalAmount: 299.95m,
            status:      "Confirmed");

        // Añadir evento de dominio antes de guardar
        order.Place();
        AnsiConsole.MarkupLine($"[grey]Orden '{order.OrderNumber}' creada con 1 domain event pendiente.[/]");
        AnsiConsole.MarkupLine($"[grey]Llamando SaveChangesAsync…[/]\n");

        db.Orders.Add(order);
        await uow.SaveChangesAsync(ct);

        AnsiConsole.MarkupLine($"\n[green]✔ Tras SaveChanges: DomainEvents.Count = {order.DomainEvents.Count}[/] " +
                               $"(limpiados por el interceptor antes del dispatch)");
        Pause();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 7 · SlowQueryInterceptor
    // ══════════════════════════════════════════════════════════════════════════

    private async Task SlowQueryInterceptorDemoAsync(CancellationToken ct)
    {
        Section("SlowQueryInterceptor — umbral de query lenta");

        AnsiConsole.MarkupLine($"[grey]Umbral actual:[/] [yellow]{SlowQueryInterceptor.SlowQueryThresholdMs} ms[/]");
        AnsiConsole.MarkupLine("[grey]Las queries que superen el umbral se registran como WARNING " +
                               "en ILogger<SlowQueryInterceptor>.[/]\n");

        var newThreshold = int.Parse(Ask($"Nuevo umbral en ms (actual: {SlowQueryInterceptor.SlowQueryThresholdMs})"));
        SlowQueryInterceptor.SlowQueryThresholdMs = newThreshold;
        Ok($"SlowQueryInterceptor.SlowQueryThresholdMs = [cyan]{newThreshold}[/]");

        AnsiConsole.MarkupLine("\n[grey]Ejecutando consulta — revisa la salida de log para warnings…[/]");
        var count = await db.Products.CountAsync(ct);
        AnsiConsole.MarkupLine($"[grey]Consulta completada. Productos visibles: {count}[/]");
        AnsiConsole.MarkupLine("\n[grey]Si la query tardó más de [yellow]{0} ms[/], verás un WARNING en los logs.[/]",
            newThreshold);

        Pause();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 8 · Filtro global SoftDelete
    // ══════════════════════════════════════════════════════════════════════════

    private async Task SoftDeleteFilterDemoAsync(CancellationToken ct)
    {
        Section("ModelBuilderExtensions.ApplySoftDeleteFilter — WHERE IsDeleted = false");

        // Crear dos productos, borrar uno
        var alive   = ClientProduct.Create("Filtro-Activo",    1m, "Filter");
        var deleted = ClientProduct.Create("Filtro-Eliminado", 2m, "Filter");

        db.Products.AddRange(alive, deleted);
        await uow.SaveChangesAsync(ct);

        db.Products.Remove(deleted);
        await uow.SaveChangesAsync(ct);

        var conFiltro   = await db.Products.Where(p => p.Category == "Filter").ToListAsync(ct);
        var sinFiltro   = await db.Products.IgnoreQueryFilters()
                                           .Where(p => p.Category == "Filter").ToListAsync(ct);

        var t = new Table().AddColumn("Consulta").AddColumn("Resultados");
        t.AddRow("Con filtro (normal)",          $"[cyan]{conFiltro.Count}[/]  — solo activos");
        t.AddRow("Sin filtro (IgnoreQueryFilters)", $"[cyan]{sinFiltro.Count}[/]  — activos + eliminados");
        AnsiConsole.Write(t);

        AnsiConsole.MarkupLine("\n[green]✔ El filtro se aplica automáticamente en todas las consultas normales.[/]");
        Pause();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 9 · Filtro global Tenant
    // ══════════════════════════════════════════════════════════════════════════

    private async Task TenantFilterDemoAsync(CancellationToken ct)
    {
        Section("ModelBuilderExtensions.ApplyTenantFilter — WHERE TenantId = @currentTenant");

        var tenantA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        var tenantB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

        // Insertar documentos para dos tenants distintos
        tenantContext.SetTenant(tenantA);
        var docA = DemoTenantDocument.Create(tenantA, "Doc-TenantA", "Contenido A");
        db.TenantDocuments.Add(docA);
        await uow.SaveChangesAsync(ct);

        tenantContext.SetTenant(tenantB);
        var docB = DemoTenantDocument.Create(tenantB, "Doc-TenantB", "Contenido B");
        db.TenantDocuments.Add(docB);
        await uow.SaveChangesAsync(ct);

        // Consultar como tenant A — solo debe ver su documento
        tenantContext.SetTenant(tenantA);
        db.ChangeTracker.Clear();
        var visibleA = await db.TenantDocuments.ToListAsync(ct);

        // Consultar como tenant B
        tenantContext.SetTenant(tenantB);
        db.ChangeTracker.Clear();
        var visibleB = await db.TenantDocuments.ToListAsync(ct);

        // Todos sin filtro
        var todos = await db.TenantDocuments.IgnoreQueryFilters().ToListAsync(ct);

        var t = new Table().AddColumn("Contexto").AddColumn("Resultados visibles");
        t.AddRow($"Tenant A ({tenantA})", $"[cyan]{visibleA.Count}[/]  ({string.Join(", ", visibleA.Select(d => d.Title))})");
        t.AddRow($"Tenant B ({tenantB})", $"[cyan]{visibleB.Count}[/]  ({string.Join(", ", visibleB.Select(d => d.Title))})");
        t.AddRow("Sin filtro (IgnoreQueryFilters)", $"[cyan]{todos.Count}[/]  (todos los tenants)");
        AnsiConsole.Write(t);

        AnsiConsole.MarkupLine("\n[green]✔ Aislamiento de tenant transparente — sin WHERE en el código de dominio.[/]");

        // Restaurar tenant original
        tenantContext.SetTenant(tenantA);
        Pause();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Helpers
    // ══════════════════════════════════════════════════════════════════════════

    private void PrintBanner()
    {
        AnsiConsole.Write(new Rule("[bold blue]KUKULCAN.Kernel.Database — Demo interactiva[/]").RuleStyle(Style.Parse("blue")));

        var providerColor = opts.Provider switch
        {
            DatabaseProvider.PostgresSql => "cyan",
            DatabaseProvider.SqlServer  => "red",
            _                           => "white"
        };

        var g = new Grid().AddColumn().AddColumn();
        g.AddRow("[grey]Proveedor:[/]", $"[{providerColor} bold]{opts.Provider}[/]");
        g.AddRow("[grey]Usuario:  [/]", $"[green]{currentUser.UserName}[/]");
        g.AddRow("[grey]Tenant:   [/]", $"[cyan]{tenantContext.TenantId}[/]");
        AnsiConsole.Write(g);
        AnsiConsole.WriteLine();
    }

    private static void Section(string title)
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule($"[blue]{title.EscapeMarkup()}[/]").RuleStyle(Style.Parse("blue dim")));
        AnsiConsole.WriteLine();
    }

    private static string Ask(string label) => AnsiConsole.Ask<string>($"[grey]{label.EscapeMarkup()}:[/]");

    private static void Ok(string msg)  => AnsiConsole.MarkupLine($"[green]✔[/] {msg}");

    private static void Pause()
    {
        AnsiConsole.Markup("\n[grey]Pulsa Enter para continuar…[/]");
        Console.ReadLine();
    }
}
