using KUKULCAN.SharedKernel.Database.Client.Client;
using KUKULCAN.SharedKernel.Database.Client.UI;
using KUKULCAN.SharedKernel.Database.Configuration;
using KUKULCAN.SharedKernel.Database.Interceptors;
using KUKULCAN.SharedKernel.Database.UnitOfWork;
using KUKULCAN.SharedKernel.Database.Abstractions;
using KUKULCAN.SharedKernel.Abstractions;
using KUKULCAN.SharedKernel.DomainEvents.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Spectre.Console;

// ── Bootstrap logger mínimo ────────────────────────────────────────────────────
using ILoggerFactory loggerFactory = LoggerFactory.Create(b =>
    b.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "[HH:mm:ss] "; })
     .SetMinimumLevel(LogLevel.Warning));   // Solo warnings y errores — SlowQuery los emitirá aquí

// ── Configuración ──────────────────────────────────────────────────────────────
var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables()
    .Build();

// ── Selección de proveedor ─────────────────────────────────────────────────────
AnsiConsole.Clear();
AnsiConsole.Write(new Rule("[bold blue]KUKULCAN.Kernel.Database — Demo[/]").RuleStyle(Style.Parse("blue")));

var providerChoice = AnsiConsole.Prompt(
    new SelectionPrompt<string>()
        .Title("Selecciona el [bold]proveedor de base de datos[/]:")
        .AddChoices(
            "PostgresSQL   — Npgsql.EntityFrameworkCore.PostgresSQL",
            "SQL Server   — Microsoft.EntityFrameworkCore.SqlServer"));

var selectedProvider = providerChoice switch
{
    var s when s.StartsWith("PostgresSQL") => DatabaseProvider.PostgresSql,
    var s when s.StartsWith("SQL Server") => DatabaseProvider.SqlServer,
    _                                      => DatabaseProvider.PostgresSql
};

var providerKey = selectedProvider switch
{
    DatabaseProvider.PostgresSql => "PostgresSQL",
    DatabaseProvider.SqlServer  => "SqlServer",
    _ => "PostgresSQL"
};

var connectionString = configuration[$"Providers:{providerKey}:ConnectionString"]
                       ?? throw new InvalidOperationException(
                           $"Falta Providers:{providerKey}:ConnectionString en appsettings.json");

AnsiConsole.MarkupLine($"[green]✔[/] Proveedor seleccionado: [cyan]{selectedProvider}[/]");

// ── DI Container ───────────────────────────────────────────────────────────────
var services = new ServiceCollection();

// Logging
services.AddSingleton(loggerFactory);
services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));

// KukulcanDatabaseOptions — se construye a partir de la sección Kukulcan:Database
// y se sobreescribe Provider y ConnectionString con la selección del usuario
services.Configure<KukulcanDatabaseOptions>(opts =>
{
    configuration.GetSection(KukulcanDatabaseOptions.SectionKey).Bind(opts);
    opts.Provider         = selectedProvider;
    opts.ConnectionString = connectionString;
});

// Stubs de infraestructura
services.AddSingleton<ConsoleCurrentUser>();
services.AddSingleton<ConsoleTenantContext>();
services.AddSingleton<ITenantContext>(sp => sp.GetRequiredService<ConsoleTenantContext>());

services.AddSingleton<ConsoleDateTimeProvider>();
services.AddSingleton<IClock>(sp => sp.GetRequiredService<ConsoleDateTimeProvider>());

services.AddSingleton<ConsoleDomainEventDispatcher>();
services.AddSingleton<IDomainEventDispatcher>(sp => sp.GetRequiredService<ConsoleDomainEventDispatcher>());

// SlowQueryInterceptor (singleton — sin estado salvo el umbral estático)
services.AddSingleton<SlowQueryInterceptor>();

// DbContext (scoped — se crea una vez por scope; en consola usamos scope único)
services.AddDbContext<ClientDbContext>();

// UnitOfWork
services.AddScoped<UnitOfWork<ClientDbContext>>();

// Menú
services.AddScoped<ConsoleMenu>(sp => new ConsoleMenu(
    sp.GetRequiredService<ClientDbContext>(),
    sp.GetRequiredService<UnitOfWork<ClientDbContext>>(),
    sp.GetRequiredService<ConsoleCurrentUser>(),
    sp.GetRequiredService<ConsoleTenantContext>(),
    sp.GetRequiredService<ConsoleDateTimeProvider>(),
    sp.GetRequiredService<IOptions<KukulcanDatabaseOptions>>().Value));

//  Ejecutar en un scope único
await using ServiceProvider sp = services.BuildServiceProvider();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

using IServiceScope scope = sp.CreateScope();

try
{
    var menu = scope.ServiceProvider.GetRequiredService<ConsoleMenu>();
    await menu.RunAsync(cts.Token);
}
catch (OperationCanceledException)
{
    AnsiConsole.MarkupLine("[grey]Operación cancelada.[/]");
}
catch (Exception ex)
{
    AnsiConsole.WriteException(ex, ExceptionFormats.ShortenPaths | ExceptionFormats.ShortenTypes);
    return 1;
}

AnsiConsole.MarkupLine("[grey]¡Hasta luego![/]");
return 0;
