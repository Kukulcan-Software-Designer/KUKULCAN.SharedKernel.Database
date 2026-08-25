using KUKULCAN.SharedKernel.Database.Abstractions;
using KUKULCAN.SharedKernel.Database.Client;
using KUKULCAN.SharedKernel.Database.Client.Client;
using KUKULCAN.SharedKernel.Database.Client.UI;
using KUKULCAN.SharedKernel.Database.Configuration;
using KUKULCAN.SharedKernel.Database.Interceptors;
using KUKULCAN.SharedKernel.Database.UnitOfWork;
using KUKULCAN.SharedKernel.Abstractions;
using KUKULCAN.SharedKernel.DomainEvents.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Spectre.Console;

using ILoggerFactory loggerFactory = LoggerFactory.Create(b =>
    b.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "[HH:mm:ss] "; })
     .SetMinimumLevel(LogLevel.Warning));

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables()
    .Build();

AnsiConsole.Clear();
AnsiConsole.Write(new Rule("[bold blue]KUKULCAN.SharedKernel.Database — Reference Client[/]").RuleStyle(Style.Parse("blue")));

var providerChoice = AnsiConsole.Prompt(
    new SelectionPrompt<string>()
        .Title("Selecciona el [bold]proveedor de base de datos[/]:")
        .AddChoices(
            "PostgreSQL — Npgsql.EntityFrameworkCore.PostgreSQL",
            "SQL Server — Microsoft.EntityFrameworkCore.SqlServer",
            "MySQL — MySql.EntityFrameworkCore"));

var selectedProvider = providerChoice switch
{
    var s when s.StartsWith("PostgreSQL") => DatabaseProvider.PostgresSql,
    var s when s.StartsWith("SQL Server") => DatabaseProvider.SqlServer,
    var s when s.StartsWith("MySQL") => DatabaseProvider.MySql,
    _ => throw new InvalidOperationException("Unknown database provider selection.")
};

var providerKey = selectedProvider switch
{
    DatabaseProvider.PostgresSql => "PostgreSql",
    DatabaseProvider.SqlServer => "SqlServer",
    DatabaseProvider.MySql => "MySql",
    _ => throw new InvalidOperationException("Unsupported database provider.")
};

var connectionString = configuration[$"Providers:{providerKey}:ConnectionString"]
    ?? throw new InvalidOperationException($"Falta Providers:{providerKey}:ConnectionString en appsettings.json");

if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException($"La connection string para {providerKey} está vacía.");

AnsiConsole.MarkupLine($"[green]✔[/] Proveedor seleccionado: [cyan]{selectedProvider}[/]");

var services = new ServiceCollection();
services.AddSingleton(loggerFactory);
services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));

services.Configure<KukulcanDatabaseOptions>(opts =>
{
    configuration.GetSection(KukulcanDatabaseOptions.SectionKey).Bind(opts);
    opts.Provider = selectedProvider;
    opts.ConnectionString = connectionString;
});

services.AddSingleton<ConsoleCurrentUser>();
services.AddScoped<ConsoleTenantContext>();
services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<ConsoleTenantContext>());
services.AddSingleton<ConsoleDateTimeProvider>();
services.AddSingleton<IClock>(sp => sp.GetRequiredService<ConsoleDateTimeProvider>());
services.AddSingleton<ConsoleDomainEventDispatcher>();
services.AddSingleton<IDomainEventDispatcher>(sp => sp.GetRequiredService<ConsoleDomainEventDispatcher>());
services.AddSingleton<SlowQueryInterceptor>();
services.AddDbContext<ClientDbContext>();
services.AddScoped<UnitOfWork<ClientDbContext>>();
services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<UnitOfWork<ClientDbContext>>());
services.AddScoped<ClientDatabaseInitializer>();
services.AddScoped<ReferenceClientScenarioRunner>();
services.AddScoped<ConsoleMenu>(sp => new ConsoleMenu(
    sp.GetRequiredService<ClientDbContext>(),
    sp.GetRequiredService<UnitOfWork<ClientDbContext>>(),
    sp.GetRequiredService<ConsoleCurrentUser>(),
    sp.GetRequiredService<ConsoleTenantContext>(),
    sp.GetRequiredService<ConsoleDateTimeProvider>(),
    sp.GetRequiredService<IOptions<KukulcanDatabaseOptions>>().Value));

await using ServiceProvider sp = services.BuildServiceProvider();
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

await using var scope = sp.CreateAsyncScope();

try
{
    var initializer = scope.ServiceProvider.GetRequiredService<ClientDatabaseInitializer>();
    await AnsiConsole.Status().StartAsync("Inicializando esquema y seed…", _ => initializer.InitializeAsync(cts.Token));
    AnsiConsole.MarkupLine("[green]✔[/] Base de datos lista.\n");

    var mode = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title("Selecciona el modo de ejecución:")
            .AddChoices(
                "Full Reference Client — ejecutar todos los casos de uso",
                "Interactive Client — menú interactivo"));

    if (mode.StartsWith("Full Reference Client"))
    {
        var runner = scope.ServiceProvider.GetRequiredService<ReferenceClientScenarioRunner>();
        await runner.RunAllAsync(cts.Token);
    }
    else
    {
        var menu = scope.ServiceProvider.GetRequiredService<ConsoleMenu>();
        await menu.RunAsync(cts.Token);
    }
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
